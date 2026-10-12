namespace AffiVideo.Worker.Rendering;

/// <summary>
/// Where the worker finds what it renders with. The defaults are where the
/// worker image puts them; FFmpeg, ffprobe and Node are on its path.
/// </summary>
public sealed class RenderingOptions
{
    public const string Section = "Rendering";

    /// <summary>The Remotion project: its packages, its browser, and its bundle in <c>bundle</c>.</summary>
    public string RemotionDirectory { get; set; } = "/app/remotion";

    /// <summary>The model that cuts a Product out of its photo, as an ONNX file.</summary>
    public string CutOutModelPath { get; set; } = "/app/models/birefnet-general-lite.onnx";

    /// <summary>Where each job keeps its temporary files, in a folder of its own that is deleted when the job ends.</summary>
    public string WorkDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "affivideo-render");

    /// <summary>How long the worker waits before looking again when nothing is queued.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How many jobs this worker renders at the same time. Each draws <see cref="ScenesAtOnce"/> Scenes at once.</summary>
    public int JobsAtOnce { get; set; } = 1;

    /// <summary>How many Scenes of one job Remotion draws at the same time.</summary>
    public int ScenesAtOnce { get; set; } = 2;

    /// <summary>
    /// How many pages one Remotion render opens to draw frames on. Left to itself Remotion opens half
    /// as many as the machine has cores, for every render: with several renders at once that is several
    /// times too many, and a page that gets no answer from its render's server fails the render. So the
    /// renders this worker runs at once share that half between them, and each has at least one.
    /// </summary>
    /// <param name="cores">How many cores the machine has.</param>
    public int PagesForEachRender(int cores) =>
        Math.Max(1, cores / 2 / (Math.Max(1, JobsAtOnce) * Math.Max(1, ScenesAtOnce)));

    /// <summary>The longest one run of Remotion or FFmpeg may take before it is stopped.</summary>
    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>A render that cannot go on, with the reason in words for the member.</summary>
public sealed class RenderFailedException(string reason) : Exception(reason);
