using AffiVideo.Application.Providers;
using AffiVideo.Domain;
using SkiaSharp;

namespace AffiVideo.Infrastructure.Generation;

// The fake image provider. It calls nobody and reads nothing of the description:
// every Still it makes is the same picture, striped so that nobody takes it for a
// photograph. What it made is labelled as fake wherever it is shown.
internal sealed class FakeImageGenerator(FakeGenerationOptions options) : IImageGenerator
{
    public const string Model = "fake-still";

    private const int Width = 1080;
    private const int Height = 1920;

    private static readonly Lazy<byte[]> Picture = new(Draw);

    private int _asked;

    public ImageProvider Provider => ImageProvider.Fake;

    public async Task<ImageGeneration> GenerateAsync(string description, CancellationToken cancellationToken)
    {
        if (options.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(options.DelaySeconds), cancellationToken);

        // It misbehaves only as it is configured to, so that what a member is shown then can be seen.
        return options.Refuses
            ? new ImageGeneration.Refused("The fake image provider refuses every picture, as it is configured to.", options.BillsFailures)
            : Interlocked.Increment(ref _asked) <= options.TechnicalFailures
                ? new ImageGeneration.Failed("The fake image provider failed, as it is configured to.", options.BillsFailures)
                : new ImageGeneration.Made(Picture.Value, Model);
    }

    // A soft vertical gradient under wide diagonal stripes.
    private static byte[] Draw()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var gradient = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(0, Height), [new SKColor(0xE8, 0xEE, 0xF4), new SKColor(0xB9, 0xC6, 0xD6)], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, Width, Height, gradient);
            // Dark enough to be told from the studio backdrop in a frame of a video, under a template's wash.
            using var stripe = new SKPaint { Color = new SKColor(0x5B, 0x6B, 0x80, 0x60), StrokeWidth = 60, IsAntialias = true };
            for (var x = -Height; x < Width; x += 240)
            {
                canvas.DrawLine(x, Height, x + Height, 0, stripe);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return png.ToArray();
    }
}

// What the worker has when no image provider is configured. A Still that reaches it was
// confirmed by an API that has one: the two are configured differently, and the member is told.
internal sealed class NoImageGenerator : IImageGenerator
{
    public ImageProvider Provider => ImageProvider.None;

    public Task<ImageGeneration> GenerateAsync(string description, CancellationToken cancellationToken) =>
        Task.FromResult<ImageGeneration>(new ImageGeneration.Failed("The worker has no image provider configured.", Billed: false));
}
