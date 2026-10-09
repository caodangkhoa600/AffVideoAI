using AffiVideo.Application.Providers;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// Cuts a Product out of its photo with BiRefNet (general, lite; MIT licence),
/// run on the CPU by ONNX Runtime inside the worker: no service is called. The
/// model only says how much of each pixel shows. The colours are the photo's.
/// </summary>
internal sealed class OnnxCutOut(IOptions<RenderingOptions> options) : IImageProcessor, IDisposable
{
    // The model looks at every photo at this size, whatever size it came in.
    private const int Side = 1024;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Deviation = [0.229f, 0.224f, 0.225f];

    // Loaded when the first photo needs it, and kept: loading takes seconds.
    private readonly Lazy<InferenceSession> _model = new(() =>
    {
        // Without these ONNX Runtime keeps every block it has ever asked for and asks for twice what it
        // needs, and the worker holds over 13 GB for a model that works in far less.
        using var frugal = new SessionOptions { EnableCpuMemArena = false, EnableMemoryPattern = false };
        return new InferenceSession(options.Value.CutOutModelPath, frugal);
    });

    // The model needs gigabytes while it runs, so one photo at a time.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public async Task<Stream> CutOutAsync(Stream photo, CancellationToken cancellationToken)
    {
        using var read = new MemoryStream();
        await photo.CopyToAsync(read, cancellationToken);
        var image = Pixels.Decode(read.ToArray());

        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            var shows = await Task.Run(() => Mask(image), cancellationToken);
            var cutOut = image with { Rgba = (byte[])image.Rgba.Clone() };
            for (var i = 0; i < shows.Length; i++) cutOut.Rgba[i * 4 + 3] = shows[i];
            return new MemoryStream(cutOut.EncodePng(), writable: false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    // How much of each pixel of the image is the Product, from 0 to 255.
    private byte[] Mask(Pixels image)
    {
        var colour = new float[image.Width * image.Height * 3];
        for (var i = 0; i < image.Width * image.Height; i++)
        {
            colour[i * 3] = image.Rgba[i * 4];
            colour[i * 3 + 1] = image.Rgba[i * 4 + 1];
            colour[i * 3 + 2] = image.Rgba[i * 4 + 2];
        }
        var small = Resampling.Resize(colour, image.Width, image.Height, 3, Side, Side);

        // As the model was trained: the brightest value becomes 1, then each channel is centred and scaled.
        var brightest = Math.Max(small.Max(), 1e-6f);
        var input = new DenseTensor<float>([1, 3, Side, Side]);
        for (var i = 0; i < Side * Side; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                input.Buffer.Span[c * Side * Side + i] = (small[i * 3 + c] / brightest - Mean[c]) / Deviation[c];
            }
        }

        var session = _model.Value;
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        var answer = results[0].AsEnumerable<float>().ToArray();

        float lowest = float.MaxValue, highest = float.MinValue;
        for (var i = 0; i < answer.Length; i++)
        {
            answer[i] = 1 / (1 + MathF.Exp(-answer[i]));
            lowest = Math.Min(lowest, answer[i]);
            highest = Math.Max(highest, answer[i]);
        }
        var range = Math.Max(highest - lowest, 1e-6f);
        for (var i = 0; i < answer.Length; i++) answer[i] = MathF.Floor((answer[i] - lowest) / range * 255);

        var full = Resampling.Resize(answer, Side, Side, 1, image.Width, image.Height);
        var mask = new byte[full.Length];
        for (var i = 0; i < full.Length; i++) mask[i] = Pixels.ToByte(full[i]);
        return mask;
    }

    public void Dispose()
    {
        if (_model.IsValueCreated) _model.Value.Dispose();
        _oneAtATime.Dispose();
    }
}
