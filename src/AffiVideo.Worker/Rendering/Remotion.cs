using System.Text.Json;
using System.Text.Json.Serialization;
using AffiVideo.Domain;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// Draws one Scene at a time with Remotion (ADR 0002). The templates are bundled
/// into the worker image when it is built; a job renders from its own copy of
/// that bundle, with its images beside it, so nothing is shared between jobs.
/// Everything a member typed reaches a template in a file of data.
/// </summary>
internal sealed class Remotion(IOptions<RenderingOptions> options)
{
    /// <summary>The folder under a bundle's public folder that holds a job's own images.</summary>
    public const string ImagesFolder = "render";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RenderingOptions _options = options.Value;

    /// <summary>Copies the bundle into the job's folder.</summary>
    /// <returns>Where the job's images go, to be named in a Scene as <c>render/…</c>.</returns>
    public string Prepare(string jobDirectory)
    {
        var bundle = Path.Combine(_options.RemotionDirectory, "bundle");
        if (!Directory.Exists(bundle))
        {
            throw new InvalidOperationException($"There is no Remotion bundle at {bundle}. It is made when the worker image is built.");
        }

        var copy = BundleOf(jobDirectory);
        foreach (var directory in Directory.EnumerateDirectories(bundle, "*", SearchOption.AllDirectories).Prepend(bundle))
        {
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(bundle, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(bundle, file)));
        }
        return Directory.CreateDirectory(Path.Combine(copy, "public", ImagesFolder)).FullName;
    }

    /// <summary>Renders the Scene to an H.264 clip with no audio, tagged BT.709.</summary>
    public async Task RenderAsync(string jobDirectory, int position, SceneInput scene, string clip, CancellationToken cancellationToken)
    {
        var props = Path.Combine(jobDirectory, $"scene-{position}.json");
        await File.WriteAllBytesAsync(props, JsonSerializer.SerializeToUtf8Bytes(scene, Json), cancellationToken);

        await Processes.RunAsync(
            "node",
            [
                Path.Combine(_options.RemotionDirectory, "node_modules", "@remotion", "cli", "remotion-cli.js"),
                "render", BundleOf(jobDirectory), "Scene", clip,
                "--props", props,
                "--codec", "h264", "--crf", "16", "--color-space", "bt709",
                "--image-format", "png", "--muted", "--log", "error",
            ],
            _options.RemotionDirectory, jobDirectory, _options.ProcessTimeout, cancellationToken);
    }

    private static string BundleOf(string jobDirectory) => Path.Combine(jobDirectory, "bundle");
}

/// <summary>One Scene as a template is handed it: <c>SceneInput</c> in remotion/src/scene.ts.</summary>
/// <param name="Lines">The Scene's on-screen text. It is data: a template sets it in type and does nothing else with it.</param>
/// <param name="Previous">What the Scene before left on screen, which this Scene carries on from.</param>
internal sealed record SceneInput(
    CreativeTemplate Template,
    SceneLayout Layout,
    int DurationInFrames,
    IReadOnlyList<string> Lines,
    LayerInput Product,
    PreviousSceneInput? Previous,
    string Backdrop,
    Palette Colours);

internal sealed record PreviousSceneInput(SceneLayout Layout, LayerInput Product);

/// <param name="File">Within the bundle's public folder.</param>
internal sealed record LayerInput(string File, int Width, int Height, int ProductWidth, int ProductHeight);
