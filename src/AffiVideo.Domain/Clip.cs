namespace AffiVideo.Domain;

/// <summary>
/// A generated video made from an approved Still, for generated footage. It is
/// confirmed with its Still, waits until a member has approved that Still, then
/// waits in the database until a worker has a video provider make it, and is then
/// the member's to watch and approve. It belongs to the Project it was made for
/// and is used by every Variant of it.
/// </summary>
public sealed class Clip : IOwnedByOrganization
{
    public const string ContentType = "video/mp4";

    // For the data-access layer, which fills the properties itself.
    private Clip()
    {
    }

    /// <param name="stillId">The approved Still it is made from. Null when that Still is not approved yet: the Clip waits for it.</param>
    /// <param name="factIds">The Facts the Scene it was asked for cites.</param>
    /// <param name="durationMs">How long the Scene it was asked for lasts, which is how long a Clip the provider is asked for.</param>
    /// <param name="amount">What making it is estimated to cost, from the rates in force as it is asked for.</param>
    /// <param name="ordinal">Which of the Clips asked for with this idempotency key it is, counted from one.</param>
    public Clip(
        Guid id, Guid organizationId, Guid projectId, Guid productId, Guid? stillId, string description, IEnumerable<Guid> factIds,
        int durationMs, VideoProvider provider, EstimatedAmount amount, string ratesVersion, string idempotencyKey, int ordinal,
        Guid requestedByMemberId, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProjectId = projectId;
        ProductId = productId;
        StillId = stillId;
        Description = description;
        FactIds = [.. factIds.Distinct().Order()];
        DurationMs = durationMs;
        Provider = provider;
        EstimatedAmount = amount.Amount;
        Currency = amount.Currency;
        RatesVersion = ratesVersion;
        IdempotencyKey = idempotencyKey;
        Ordinal = ordinal;
        RequestedByMemberId = requestedByMemberId;
        State = stillId is null ? ClipState.WaitingForStill : ClipState.Queued;
        AvailableAt = createdAt;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The Project it was made for. Absent once that Project has been deleted: the Clip is kept.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>The Product of that Project, which the cost of the Clip is recorded against.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The approved Still it is made from: exactly one. Absent while it waits for that Still to be approved.</summary>
    public Guid? StillId { get; private set; }

    /// <summary>What it is to show, as the provider is told: the description of its Still.</summary>
    public string Description { get; private set; } = "";

    /// <summary>The Facts the picture rests on: those its Scene cited as it was asked for, in a fixed order.</summary>
    public Guid[] FactIds { get; private set; } = [];

    /// <summary>How long a Clip the provider is asked for, in milliseconds: its Scene's length.</summary>
    public int DurationMs { get; private set; }

    /// <summary>What makes it, fixed as it is asked for.</summary>
    public VideoProvider Provider { get; private set; }

    /// <summary>The provider's model that made it. Absent until it is made.</summary>
    public string? Model { get; private set; }

    public ClipState State { get; private set; }

    /// <summary>What making it is estimated to cost. Nobody was billed this.</summary>
    public decimal EstimatedAmount { get; private set; }

    public string Currency { get; private set; } = "";

    /// <summary>The version of the rates the amount was estimated with.</summary>
    public string RatesVersion { get; private set; } = "";

    /// <summary>What the member's click was submitted with, so that a request sent twice asks for one Clip.</summary>
    public string IdempotencyKey { get; private set; } = "";

    public int Ordinal { get; private set; }

    public Guid RequestedByMemberId { get; private set; }

    /// <summary>How many times a worker has taken it.</summary>
    public int Attempt { get; private set; }

    public DateTimeOffset? AttemptStartedAt { get; private set; }

    /// <summary>The earliest a worker may take it while it is queued: later than now when it waits to be tried again.</summary>
    public DateTimeOffset AvailableAt { get; private set; }

    /// <summary>When a Clip that waits to be tried again may next be taken. Absent for any other.</summary>
    public DateTimeOffset? RetryAt => State == ClipState.Queued && Attempt > 0 ? AvailableAt : null;

    /// <summary>Names the one taking that may still write to it, as a Render job's does.</summary>
    public Guid? LeaseId { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public ClipFailureKind? FailureKind { get; private set; }

    /// <summary>Why it was not made, in words for the member.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>What went wrong as the provider or the program reported it.</summary>
    public string? FailureDetail { get; private set; }

    /// <summary>
    /// Whether the provider is taken to have charged for it. A Clip that was made was charged for,
    /// approved or not; one that failed was only if the provider says so.
    /// </summary>
    public bool Billed { get; private set; }

    /// <summary>Of the stored video. Zero until it is made.</summary>
    public long SizeInBytes { get; private set; }

    public Guid? ApprovedByMemberId { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the state last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Where the video is kept, under its Organization.</summary>
    public string StorageKey => $"organizations/{OrganizationId}/clips/{Id}.mp4";

    /// <summary>Whether a fake provider made it, or will: it is then footage of nothing, and is labelled so.</summary>
    public bool Fake => Provider == VideoProvider.Fake;

    /// <summary>Whether a Scene that asks for this footage has it: it waits for its Still, is on its way, waits for the member, or is approved.</summary>
    public bool IsCurrent => State != ClipState.Failed;

    /// <summary>Whether its footage was made and stored, to be watched.</summary>
    public bool HasFootage => State is ClipState.ReadyForReview or ClipState.Approved;

    /// <summary>
    /// Whether its amount counts toward what the Project has spent: everything but a failure the provider did
    /// not charge for. A Clip that waits for its Still counts already, for as long as a Still is on its way
    /// for it: it was confirmed, and will be charged for. Whether one is, is for whoever knows the Project's Stills.
    /// </summary>
    public bool CountsAsSpent => State != ClipState.Failed || Billed;

    /// <summary>
    /// Whether this is the footage the Scene asks for: generated footage of the picture the Scene
    /// describes, resting on the Facts the Scene cites, and lasting at least as long as the Scene does.
    /// </summary>
    public bool Shows(Scene scene) =>
        scene.Technique == Technique.GeneratedFootage && Description == scene.Description
        && FactIds.SequenceEqual(scene.CitedFactIds) && DurationMs >= scene.DurationMs;

    /// <summary>The Still it waited for is approved: it is made from that one, and may be taken.</summary>
    /// <returns>False, with nothing changed, unless it was waiting for its Still.</returns>
    public bool StartFrom(Guid stillId, DateTimeOffset now)
    {
        if (State != ClipState.WaitingForStill || !Become(ClipState.Queued, now)) return false;
        StillId = stillId;
        AvailableAt = now;
        return true;
    }

    /// <summary>The video is made and stored: it is the member's to watch and approve.</summary>
    /// <returns>False, with nothing changed, unless a worker was making it.</returns>
    public bool Made(string model, long sizeInBytes, DateTimeOffset now)
    {
        if (!Become(ClipState.ReadyForReview, now)) return false;
        Model = model;
        SizeInBytes = sizeInBytes;
        Billed = true;
        return true;
    }

    /// <summary>Puts a Clip that a worker was making back in the queue, to be taken again.</summary>
    /// <returns>False, with nothing changed, unless a worker was making it.</returns>
    public bool Requeue(DateTimeOffset availableAt, DateTimeOffset now)
    {
        if (State != ClipState.Generating || !Become(ClipState.Queued, now)) return false;
        AvailableAt = availableAt;
        return true;
    }

    /// <returns>False, with nothing changed, unless it was queued or being made.</returns>
    public bool Fail(ClipFailureKind kind, string message, string? detail, bool billed, DateTimeOffset now)
    {
        if (!Become(ClipState.Failed, now)) return false;
        FailureKind = kind;
        FailureMessage = Shortened(message, RenderJob.FailureMessageMaxLength);
        FailureDetail = detail is null ? null : Shortened(detail, RenderJob.FailureDetailMaxLength);
        Billed = billed;
        return true;
    }

    /// <returns>False, with nothing changed, unless it was waiting for the member.</returns>
    public bool Approve(Guid memberId, DateTimeOffset now)
    {
        if (!Become(ClipState.Approved, now)) return false;
        ApprovedByMemberId = memberId;
        ApprovedAt = now;
        return true;
    }

    private bool Become(ClipState state, DateTimeOffset now)
    {
        if (!State.CanBecome(state)) return false;
        State = state;
        UpdatedAt = now;
        // Only a Clip that a worker is making is anyone's.
        if (state != ClipState.Generating)
        {
            LeaseId = null;
            LeaseExpiresAt = null;
        }
        return true;
    }

    private static string Shortened(string text, int length) => text.Length > length ? text[..length] : text;
}

/// <summary>Where a Clip is.</summary>
public enum ClipState
{
    /// <summary>Confirmed, and waiting for a member to approve the Still it is to be made from.</summary>
    WaitingForStill,

    /// <summary>Waiting for a worker.</summary>
    Queued,

    /// <summary>A worker has asked the provider for it.</summary>
    Generating,

    /// <summary>Made, and waiting for a member to watch and approve it.</summary>
    ReadyForReview,

    /// <summary>A member accepted it: it may be used.</summary>
    Approved,

    /// <summary>It was not made.</summary>
    Failed,
}

/// <summary>Why a Clip was not made, which decides what the member can do about it.</summary>
public enum ClipFailureKind
{
    /// <summary>The provider would not make this footage. Change the description.</summary>
    Refused,

    /// <summary>Something went wrong at the provider or on the way to it, as often as a Clip is tried. Ask again.</summary>
    Technical,

    /// <summary>The worker stopped while it was making it. Whether the provider charged is not known, so it is taken to have.</summary>
    WorkerLost,
}

public static class ClipStates
{
    public static bool CanBecome(this ClipState from, ClipState to) => (from, to) switch
    {
        (ClipState.WaitingForStill, ClipState.Queued) => true,
        (ClipState.Queued, ClipState.Generating) => true,
        (ClipState.Generating, ClipState.Queued or ClipState.ReadyForReview) => true,
        (ClipState.Queued or ClipState.Generating, ClipState.Failed) => true,
        (ClipState.ReadyForReview, ClipState.Approved) => true,
        _ => false,
    };
}
