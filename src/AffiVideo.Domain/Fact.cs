namespace AffiVideo.Domain;

/// <summary>
/// A single statement about a Product, in one language. Its text, language and
/// source never change: a member who wants it to say something else withdraws it
/// and proposes another.
/// </summary>
public sealed class Fact : IOwnedByOrganization
{
    public const int TextMaxLength = 500;
    public const int SourceMaxLength = 500;

    // For the data-access layer, which fills the properties itself.
    private Fact()
    {
    }

    public Fact(Guid id, Guid organizationId, Guid productId, FactDetails details, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProductId = productId;
        Text = details.Text.Trim();
        Language = details.Language;
        Source = string.IsNullOrWhiteSpace(details.Source) ? null : details.Source.Trim();
        State = FactState.Proposed;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Text { get; private set; } = "";

    /// <summary>One of <see cref="ContentLanguages.Codes"/>.</summary>
    public string Language { get; private set; } = "";

    /// <summary>Where the Fact came from, in the member's own words, so that it can be checked again.</summary>
    public string? Source { get; private set; }

    public FactState State { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The member who Confirmed it. Kept when the Fact is later Withdrawn.</summary>
    public Guid? ConfirmedByMemberId { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <returns>False, with nothing changed, unless the Fact is Proposed.</returns>
    public bool Confirm(Guid memberId, DateTimeOffset now)
    {
        if (!State.CanBecome(FactState.Confirmed)) return false;
        State = FactState.Confirmed;
        ConfirmedByMemberId = memberId;
        ConfirmedAt = now;
        return true;
    }

    /// <returns>False, with nothing changed, when the Fact is already Withdrawn.</returns>
    public bool Withdraw(DateTimeOffset now)
    {
        if (!State.CanBecome(FactState.Withdrawn)) return false;
        State = FactState.Withdrawn;
        WithdrawnAt = now;
        return true;
    }
}

/// <summary>Everything a member types in about a Fact.</summary>
public sealed record FactDetails(string Text, string Language, string? Source);

public enum FactState
{
    /// <summary>Suggested, and not to be used by a script yet.</summary>
    Proposed,

    /// <summary>Accepted by a member of the Organization, which is responsible for its truth.</summary>
    Confirmed,

    /// <summary>No longer to be used. Nothing follows this state.</summary>
    Withdrawn,
}

public static class FactStates
{
    /// <summary>Proposed to Confirmed, Proposed to Withdrawn and Confirmed to Withdrawn, and nothing else.</summary>
    public static bool CanBecome(this FactState from, FactState to) => (from, to) is
        (FactState.Proposed, FactState.Confirmed) or
        (FactState.Proposed, FactState.Withdrawn) or
        (FactState.Confirmed, FactState.Withdrawn);
}

/// <summary>The languages a video's content can be written in, as ISO 639-1 codes.</summary>
public static class ContentLanguages
{
    public const string Vietnamese = "vi";
    public const string English = "en";
    public const int CodeMaxLength = 10;

    public static IReadOnlyList<string> Codes { get; } = [Vietnamese, English];

    /// <summary>The languages a video can be made in. Facts can be kept in more than these.</summary>
    public static IReadOnlyList<string> VideoCodes { get; } = [Vietnamese];
}
