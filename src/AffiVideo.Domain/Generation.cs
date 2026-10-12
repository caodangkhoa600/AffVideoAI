namespace AffiVideo.Domain;

/// <summary>What makes Stills.</summary>
public enum ImageProvider
{
    /// <summary>Nothing does.</summary>
    None,

    /// <summary>The fake: it returns a fixed picture and calls nobody. What it made is labelled as fake.</summary>
    Fake,
}

/// <summary>What makes Clips from Stills.</summary>
public enum VideoProvider
{
    /// <summary>Nothing does.</summary>
    None,

    /// <summary>The fake: it returns a test pattern and calls nobody. What it made is labelled as fake.</summary>
    Fake,
}

/// <summary>
/// The Production cost record of one generated thing: what making it is estimated
/// to have cost, by which provider and model. One is written as the thing is made
/// or fails for good, whatever a member then does with it, and nothing about it
/// changes afterwards. It is an estimate from the rates in force as the thing was
/// asked for, never an amount anyone was billed.
/// </summary>
public sealed class GenerationCostRecord : IOwnedByOrganization
{
    public const int ProviderMaxLength = 50;

    // For the data-access layer, which fills the properties itself.
    private GenerationCostRecord()
    {
    }

    /// <summary>The record of a Still as it stands now: made, or failed.</summary>
    public GenerationCostRecord(Guid id, Still still, DateTimeOffset recordedAt)
    {
        Id = id;
        OrganizationId = still.OrganizationId;
        ProductId = still.ProductId;
        StillId = still.Id;
        Provider = still.Provider.ToString();
        Model = still.Model;
        // What the provider did not charge for cost nothing.
        EstimatedAmount = still.Billed ? still.EstimatedAmount : 0m;
        Currency = still.Currency;
        RatesVersion = still.RatesVersion;
        RecordedAt = recordedAt;
    }

    /// <summary>The record of a Clip as it stands now: made, or failed. It is the Clip's alone, and is never split across the videos that use it.</summary>
    public GenerationCostRecord(Guid id, Clip clip, DateTimeOffset recordedAt)
    {
        Id = id;
        OrganizationId = clip.OrganizationId;
        ProductId = clip.ProductId;
        ClipId = clip.Id;
        Provider = clip.Provider.ToString();
        Model = clip.Model;
        // What the provider did not charge for cost nothing.
        EstimatedAmount = clip.Billed ? clip.EstimatedAmount : 0m;
        Currency = clip.Currency;
        RatesVersion = clip.RatesVersion;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The Product the thing was generated for. The record stays with it whatever becomes of the thing.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The Still the record is of, when it is of one.</summary>
    public Guid? StillId { get; private set; }

    /// <summary>The Clip the record is of, when it is of one.</summary>
    public Guid? ClipId { get; private set; }

    /// <summary>What generated it, by name.</summary>
    public string Provider { get; private set; } = "";

    /// <summary>The provider's model. Absent when nothing was made.</summary>
    public string? Model { get; private set; }

    /// <summary>What it is estimated to have cost. Nobody was billed this.</summary>
    public decimal EstimatedAmount { get; private set; }

    public string Currency { get; private set; } = "";

    /// <summary>The version of the rates the amount was estimated with.</summary>
    public string RatesVersion { get; private set; } = "";

    public DateTimeOffset RecordedAt { get; private set; }
}
