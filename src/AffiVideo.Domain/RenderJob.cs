namespace AffiVideo.Domain;

/// <summary>
/// The work of rendering one Storyboard version into a Rendered Video. It waits
/// in the database until a worker takes it, and its state is all the progress
/// a member is shown.
/// </summary>
public sealed class RenderJob : IOwnedByOrganization
{
    public const int IdempotencyKeyMaxLength = 100;
    public const int FailureMessageMaxLength = 1000;
    public const int FailureDetailMaxLength = 4000;

    // For the data-access layer, which fills the properties itself.
    private RenderJob()
    {
    }

    /// <summary>A job is queued as soon as it is made, and may be taken at once.</summary>
    public RenderJob(Guid id, Guid organizationId, Guid storyboardId, string idempotencyKey, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        StoryboardId = storyboardId;
        IdempotencyKey = idempotencyKey;
        State = RenderJobState.Queued;
        AvailableAt = createdAt;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The Storyboard version being rendered.</summary>
    public Guid StoryboardId { get; private set; }

    /// <summary>
    /// What the member's click was submitted with. A Storyboard version has one job
    /// for each key, so a request that is sent twice queues one job.
    /// </summary>
    public string IdempotencyKey { get; private set; } = "";

    public RenderJobState State { get; private set; }

    /// <summary>How many times a worker has taken the job. A job that is tried again is taken again.</summary>
    public int Attempt { get; private set; }

    /// <summary>The earliest a worker may take the job while it is queued: later than now when it waits to be tried again.</summary>
    public DateTimeOffset AvailableAt { get; private set; }

    /// <summary>When a job that waits to be tried again may next be taken. Absent for any other job.</summary>
    public DateTimeOffset? RetryAt => State == RenderJobState.Queued && Attempt > 0 ? AvailableAt : null;

    /// <summary>
    /// Names the one taking of the job that may still write to it. A worker that
    /// holds an older lease has lost the job and writes nothing.
    /// </summary>
    public Guid? LeaseId { get; private set; }

    /// <summary>
    /// Until when the job is its worker's. The worker moves this on while it works;
    /// once it has passed, the job goes back to the queue.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    /// <summary>The stage the job failed in. Only a failed job has one, as with the rest of the failure.</summary>
    public RenderJobState? FailureStage { get; private set; }

    public RenderFailureCategory? FailureCategory { get; private set; }

    /// <summary>Why the job failed, in words for the member.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>What went wrong as the program reported it, for whoever looks into it.</summary>
    public string? FailureDetail { get; private set; }

    /// <summary>What the job made. Only a completed job has one, until a member deletes it.</summary>
    public Guid? RenderedVideoId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the state last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Moves to the next stage of the work. Ending the job is <see cref="Fail"/>,
    /// <see cref="Complete"/> or <see cref="Cancel"/>.
    /// </summary>
    /// <returns>False, with nothing changed, when the job cannot go from its state to that one.</returns>
    public bool MoveTo(RenderJobState state, DateTimeOffset now) => state.IsRunning() && Become(state, now);

    /// <summary>Puts a job that a worker was running back in the queue, to be taken again.</summary>
    /// <param name="availableAt">The earliest it may be taken.</param>
    /// <returns>False, with nothing changed, unless a worker was running the job.</returns>
    public bool Requeue(DateTimeOffset availableAt, DateTimeOffset now)
    {
        if (!Become(RenderJobState.Queued, now)) return false;
        AvailableAt = availableAt;
        return true;
    }

    /// <returns>False, with nothing changed, when the job has already ended.</returns>
    public bool Fail(RenderFailure failure, DateTimeOffset now)
    {
        if (!Become(RenderJobState.Failed, now)) return false;
        FailureStage = failure.Stage;
        FailureCategory = failure.Category;
        FailureMessage = Shortened(failure.Message, FailureMessageMaxLength);
        FailureDetail = failure.Detail is null ? null : Shortened(failure.Detail, FailureDetailMaxLength);
        return true;
    }

    /// <returns>False, with nothing changed, when the job has already ended.</returns>
    public bool Cancel(DateTimeOffset now) => Become(RenderJobState.Cancelled, now);

    /// <returns>False, with nothing changed, unless the job is in quality review.</returns>
    public bool Complete(Guid renderedVideoId, DateTimeOffset now)
    {
        if (!Become(RenderJobState.Completed, now)) return false;
        RenderedVideoId = renderedVideoId;
        return true;
    }

    /// <summary>
    /// The Rendered Video the job made has been deleted. The job stays completed,
    /// as the record of the render, with nothing to show for it.
    /// </summary>
    public void ForgetRenderedVideo() => RenderedVideoId = null;

    private bool Become(RenderJobState state, DateTimeOffset now)
    {
        if (!State.CanBecome(state)) return false;
        State = state;
        UpdatedAt = now;
        // Only a job that a worker is running is anyone's.
        if (!state.IsRunning())
        {
            LeaseId = null;
            LeaseExpiresAt = null;
        }
        return true;
    }

    private static string Shortened(string text, int length) => text.Length > length ? text[..length] : text;
}

/// <summary>Why a job failed.</summary>
/// <param name="Stage">The stage it was in.</param>
/// <param name="Message">In words for the member.</param>
/// <param name="Detail">As the program reported it, for whoever looks into it.</param>
public sealed record RenderFailure(RenderJobState Stage, RenderFailureCategory Category, string Message, string? Detail);

/// <summary>The kind of thing that went wrong, which decides whether trying again could help.</summary>
public enum RenderFailureCategory
{
    /// <summary>The Storyboard version cannot be rendered as it is. Rendering it again would fail the same way.</summary>
    InvalidInput,

    /// <summary>Something went wrong in the worker or in a program it runs.</summary>
    Internal,

    /// <summary>A program the worker runs took longer than it is allowed.</summary>
    Timeout,

    /// <summary>The worker stopped while it had the job, as many times as a job is tried.</summary>
    WorkerLost,
}

/// <summary>Where a render job is. The stages are in the order the work goes through them.</summary>
public enum RenderJobState
{
    Created,

    /// <summary>Waiting for a worker.</summary>
    Queued,

    /// <summary>Checking that the Storyboard version can still be rendered.</summary>
    Validating,

    /// <summary>Working out what each Scene is drawn from.</summary>
    Planning,

    /// <summary>Cutting the Product out of its photos.</summary>
    GeneratingAssets,

    /// <summary>Generating video for the Scenes that ask for it. Never entered in Product Lock.</summary>
    GeneratingVideo,

    /// <summary>Drawing the Scenes and joining them.</summary>
    Rendering,

    /// <summary>Checking the finished file.</summary>
    QualityReview,

    /// <summary>The Rendered Video is ready for review.</summary>
    Completed,

    Failed,

    Cancelled,
}

public static class RenderJobStates
{
    /// <summary>
    /// Each stage to the next, with generating video left out when there is none to
    /// generate; from any stage a worker runs, back to queued, to be tried again; and
    /// from anything that has not ended, to failed or cancelled.
    /// </summary>
    public static bool CanBecome(this RenderJobState from, RenderJobState to) => (from, to) switch
    {
        (RenderJobState.Created, RenderJobState.Queued) => true,
        (RenderJobState.Queued, RenderJobState.Validating) => true,
        (RenderJobState.Validating, RenderJobState.Planning) => true,
        (RenderJobState.Planning, RenderJobState.GeneratingAssets) => true,
        (RenderJobState.GeneratingAssets, RenderJobState.GeneratingVideo or RenderJobState.Rendering) => true,
        (RenderJobState.GeneratingVideo, RenderJobState.Rendering) => true,
        (RenderJobState.Rendering, RenderJobState.QualityReview) => true,
        (RenderJobState.QualityReview, RenderJobState.Completed) => true,
        (_, RenderJobState.Queued) => from.IsRunning(),
        (_, RenderJobState.Failed or RenderJobState.Cancelled) => !from.HasEnded(),
        _ => false,
    };

    /// <summary>A stage of the work itself, which a worker is doing: from validating to quality review.</summary>
    public static bool IsRunning(this RenderJobState state) =>
        state is >= RenderJobState.Validating and <= RenderJobState.QualityReview;

    /// <summary>Completed, failed or cancelled: nothing follows.</summary>
    public static bool HasEnded(this RenderJobState state) =>
        state is RenderJobState.Completed or RenderJobState.Failed or RenderJobState.Cancelled;
}
