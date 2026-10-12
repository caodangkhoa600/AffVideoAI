using AffiVideo.Domain;

namespace AffiVideo.Application.Providers;

/// <summary>
/// What generates pictures for Hybrid. With nothing configured, nothing is
/// generated: Hybrid plans what Product Lock does, and generation says it is unavailable.
/// </summary>
public sealed class GenerationOptions
{
    public const string Section = "Generation";

    /// <summary>What makes Stills.</summary>
    public ImageProvider ImageProvider { get; set; } = ImageProvider.None;

    /// <summary>Whether there is anything to make a Still with.</summary>
    public bool CanGenerateStills => ImageProvider != ImageProvider.None;

    /// <summary>How the fake providers misbehave, for seeing what a member is shown when a real one does.</summary>
    public FakeGenerationOptions Fake { get; set; } = new();
}

/// <summary>How the fake providers misbehave. With nothing set, they make what they are asked for every time.</summary>
public sealed class FakeGenerationOptions
{
    /// <summary>How many times the fake fails for a technical reason before it starts making what it is asked for.</summary>
    public int TechnicalFailures { get; set; }

    /// <summary>Whether a failure is one the provider charged for all the same.</summary>
    public bool BillsFailures { get; set; }

    /// <summary>Whether the fake refuses everything, as a provider refuses a picture its rules do not allow.</summary>
    public bool Refuses { get; set; }

    /// <summary>How long the fake takes to answer, as a provider takes a while.</summary>
    public double DelaySeconds { get; set; }
}
