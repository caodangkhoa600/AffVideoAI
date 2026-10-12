namespace AffiVideo.Domain;

/// <summary>
/// A generated image that a member approves before it is used. It is asked for
/// with a description, waits in the database until a worker has an image provider
/// make it, and is then the member's to approve or to replace with another. It
/// belongs to the Project it was made for and is used by every Variant of it.
/// </summary>
public sealed class Still : IOwnedByOrganization
{
    public const string ContentType = "image/png";
    public const int ModelMaxLength = 100;

    // For the data-access layer, which fills the properties itself.
    private Still()
    {
    }

    /// <summary>A Still is queued as soon as it is asked for, and may be taken at once.</summary>
    /// <param name="amount">What making it is estimated to cost, from the rates in force as it is asked for.</param>
    /// <param name="ordinal">Which of the Stills asked for with this idempotency key it is, counted from one.</param>
    public Still(
        Guid id, Guid organizationId, Guid projectId, Guid productId, Technique technique, string description,
        ImageProvider provider, EstimatedAmount amount, string ratesVersion, string idempotencyKey, int ordinal,
        Guid requestedByMemberId, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProjectId = projectId;
        ProductId = productId;
        Technique = technique;
        Description = description;
        Provider = provider;
        EstimatedAmount = amount.Amount;
        Currency = amount.Currency;
        RatesVersion = ratesVersion;
        IdempotencyKey = idempotencyKey;
        Ordinal = ordinal;
        RequestedByMemberId = requestedByMemberId;
        State = StillState.Queued;
        AvailableAt = createdAt;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The Project it was made for. Absent once that Project has been deleted: the Still is kept.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>The Product of that Project, which the cost of the Still is recorded against.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The Technique it is for: a generated backdrop as it is, or the start of a Clip for generated footage.</summary>
    public Technique Technique { get; private set; }

    /// <summary>What it is to show, as the provider is told.</summary>
    public string Description { get; private set; } = "";

    /// <summary>What makes it, fixed as it is asked for.</summary>
    public ImageProvider Provider { get; private set; }

    /// <summary>The provider's model that made it. Absent until it is made.</summary>
    public string? Model { get; private set; }

    public StillState State { get; private set; }

    /// <summary>What making it is estimated to cost. Nobody was billed this.</summary>
    public decimal EstimatedAmount { get; private set; }

    public string Currency { get; private set; } = "";

    /// <summary>The version of the rates the amount was estimated with.</summary>
    public string RatesVersion { get; private set; } = "";

    /// <summary>What the member's click was submitted with, so that a request sent twice asks for one Still.</summary>
    public string IdempotencyKey { get; private set; } = "";

    public int Ordinal { get; private set; }

    public Guid RequestedByMemberId { get; private set; }

    /// <summary>How many times a worker has taken it.</summary>
    public int Attempt { get; private set; }

    public DateTimeOffset? AttemptStartedAt { get; private set; }

    /// <summary>The earliest a worker may take it while it is queued: later than now when it waits to be tried again.</summary>
    public DateTimeOffset AvailableAt { get; private set; }

    /// <summary>When a Still that waits to be tried again may next be taken. Absent for any other.</summary>
    public DateTimeOffset? RetryAt => State == StillState.Queued && Attempt > 0 ? AvailableAt : null;

    /// <summary>Names the one taking that may still write to it, as a Render job's does.</summary>
    public Guid? LeaseId { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public StillFailureKind? FailureKind { get; private set; }

    /// <summary>Why it was not made, in words for the member.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>What went wrong as the provider or the program reported it.</summary>
    public string? FailureDetail { get; private set; }

    /// <summary>
    /// Whether the provider is taken to have charged for it. A Still that was made was charged for,
    /// approved or not; one that failed was only if the provider says so.
    /// </summary>
    public bool Billed { get; private set; }

    /// <summary>Of the stored image. Zero until it is made.</summary>
    public long SizeInBytes { get; private set; }

    public Guid? ApprovedByMemberId { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the state last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Where the image is kept, under its Organization.</summary>
    public string StorageKey => $"organizations/{OrganizationId}/stills/{Id}.png";

    /// <summary>Whether a fake provider made it, or will: it is then no picture of anything, and is labelled so.</summary>
    public bool Fake => Provider == ImageProvider.Fake;

    /// <summary>Whether a Scene that asks for this picture has it: it is on its way, waiting for the member, or approved.</summary>
    public bool IsCurrent => State is StillState.Queued or StillState.Generating or StillState.ReadyForReview or StillState.Approved;

    /// <summary>Whether its picture was made and stored, whatever a member has done with it since.</summary>
    public bool HasPicture => State is StillState.ReadyForReview or StillState.Approved or StillState.Rejected;

    /// <summary>Whether its amount counts toward what the Project has spent: everything but a failure the provider did not charge for.</summary>
    public bool CountsAsSpent => State != StillState.Failed || Billed;

    /// <summary>The image is made and stored: it is the member's to approve.</summary>
    /// <returns>False, with nothing changed, unless a worker was making it.</returns>
    public bool Made(string model, long sizeInBytes, DateTimeOffset now)
    {
        if (!Become(StillState.ReadyForReview, now)) return false;
        Model = model;
        SizeInBytes = sizeInBytes;
        Billed = true;
        return true;
    }

    /// <summary>Puts a Still that a worker was making back in the queue, to be taken again.</summary>
    /// <returns>False, with nothing changed, unless a worker was making it.</returns>
    public bool Requeue(DateTimeOffset availableAt, DateTimeOffset now)
    {
        if (!Become(StillState.Queued, now)) return false;
        AvailableAt = availableAt;
        return true;
    }

    /// <returns>False, with nothing changed, unless it was queued or being made.</returns>
    public bool Fail(StillFailureKind kind, string message, string? detail, bool billed, DateTimeOffset now)
    {
        if (!Become(StillState.Failed, now)) return false;
        FailureKind = kind;
        FailureMessage = Shortened(message, RenderJob.FailureMessageMaxLength);
        FailureDetail = detail is null ? null : Shortened(detail, RenderJob.FailureDetailMaxLength);
        Billed = billed;
        return true;
    }

    /// <returns>False, with nothing changed, unless it was waiting for the member.</returns>
    public bool Approve(Guid memberId, DateTimeOffset now)
    {
        if (!Become(StillState.Approved, now)) return false;
        ApprovedByMemberId = memberId;
        ApprovedAt = now;
        return true;
    }

    /// <summary>The member asked for another in its place. It is kept, with what it cost.</summary>
    /// <returns>False, with nothing changed, unless it was made: waiting for the member, or approved.</returns>
    public bool Reject(DateTimeOffset now) => Become(StillState.Rejected, now);

    private bool Become(StillState state, DateTimeOffset now)
    {
        if (!State.CanBecome(state)) return false;
        State = state;
        UpdatedAt = now;
        // Only a Still that a worker is making is anyone's.
        if (state != StillState.Generating)
        {
            LeaseId = null;
            LeaseExpiresAt = null;
        }
        return true;
    }

    private static string Shortened(string text, int length) => text.Length > length ? text[..length] : text;
}

/// <summary>Where a Still is.</summary>
public enum StillState
{
    /// <summary>Waiting for a worker.</summary>
    Queued,

    /// <summary>A worker has asked the provider for it.</summary>
    Generating,

    /// <summary>Made, and waiting for a member to approve it or ask for another.</summary>
    ReadyForReview,

    /// <summary>A member accepted it: it may be used.</summary>
    Approved,

    /// <summary>A member asked for another in its place.</summary>
    Rejected,

    /// <summary>It was not made.</summary>
    Failed,
}

/// <summary>Why a Still was not made, which decides what the member can do about it.</summary>
public enum StillFailureKind
{
    /// <summary>The provider would not make this picture. Change the description.</summary>
    Refused,

    /// <summary>Something went wrong at the provider or on the way to it, as often as a Still is tried. Ask again.</summary>
    Technical,

    /// <summary>The worker stopped while it was making it. Whether the provider charged is not known, so it is taken to have.</summary>
    WorkerLost,
}

public static class StillStates
{
    public static bool CanBecome(this StillState from, StillState to) => (from, to) switch
    {
        (StillState.Queued, StillState.Generating) => true,
        (StillState.Generating, StillState.Queued or StillState.ReadyForReview) => true,
        (StillState.Queued or StillState.Generating, StillState.Failed) => true,
        (StillState.ReadyForReview, StillState.Approved) => true,
        (StillState.ReadyForReview or StillState.Approved, StillState.Rejected) => true,
        _ => false,
    };
}

public static class GenerationCeilings
{
    /// <summary>
    /// Why this generation is refused, in words for the member: with it, the Project would
    /// have spent more on generation than its ceiling. Null when it may go ahead.
    /// </summary>
    /// <param name="spent">What generation for the Project is estimated to have cost so far, with what is on its way.</param>
    /// <param name="asked">What this generation is estimated to cost.</param>
    public static string? Refusal(decimal spent, decimal asked, decimal ceiling, string currency) =>
        spent + asked <= ceiling
            ? null
            : $"This would take the Project's generation to {Money(spent + asked)} {currency}, past its ceiling of {Money(ceiling)} {currency}: " +
              $"{Money(spent)} {currency} has been spent and this is estimated at {Money(asked)} {currency}. Nothing was generated.";

    /// <summary>An amount as a member reads it: cents, and finer only when there is something finer.</summary>
    public static string Money(decimal amount) => amount.ToString("0.00####", System.Globalization.CultureInfo.InvariantCulture);
}
