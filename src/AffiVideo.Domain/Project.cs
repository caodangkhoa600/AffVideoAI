namespace AffiVideo.Domain;

/// <summary>
/// One Product plus one brief. The brief is fixed when the Project is created:
/// nothing changes it afterwards.
/// </summary>
public sealed class Project : IOwnedByOrganization
{
    public const int AudienceMaxLength = 500;
    public const int ObjectiveMaxLength = 500;
    public const int MinTargetDurationSeconds = 15;
    public const int MaxTargetDurationSeconds = 30;
    public const int VoiceMaxLength = 50;

    /// <summary>The voice there is when none is configured, and the one every Project had before a brief named one.</summary>
    public const string BuiltInVoice = "sarah";

    // For the data-access layer, which fills the properties itself.
    private Project()
    {
    }

    /// <param name="voice">The voice the brief names, or the Organization's when it names none.</param>
    public Project(Guid id, Guid organizationId, Guid productId, ProjectBrief brief, string voice, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProductId = productId;
        Audience = brief.Audience.Trim();
        Language = brief.Language;
        TargetDurationSeconds = brief.TargetDurationSeconds;
        Objective = brief.Objective.Trim();
        Voice = voice;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>Who the videos are for.</summary>
    public string Audience { get; private set; } = "";

    /// <summary>One of <see cref="ContentLanguages.VideoCodes"/>.</summary>
    public string Language { get; private set; } = "";

    /// <summary>How long each video is to be, from 15 to 30 seconds.</summary>
    public int TargetDurationSeconds { get; private set; }

    /// <summary>What the videos are meant to get the viewer to do.</summary>
    public string Objective { get; private set; } = "";

    /// <summary>The voice narration text is spoken in, by its id in the list of voices on offer.</summary>
    public string Voice { get; private set; } = BuiltInVoice;

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Everything a member types in about a Project, apart from which Product it is for.</summary>
/// <param name="Voice">The voice narration text is spoken in. Left out, it is the Organization's.</param>
public sealed record ProjectBrief(string Audience, string Language, int TargetDurationSeconds, string Objective, string? Voice = null);
