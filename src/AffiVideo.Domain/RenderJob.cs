namespace AffiVideo.Domain;

/// <summary>
/// The work of rendering one Storyboard version into a Rendered Video. It waits
/// in the database until the worker takes it, and its state is all the progress
/// a member is shown.
/// </summary>
public sealed class RenderJob : IOwnedByOrganization
{
    public const int FailureReasonMaxLength = 1000;

    // For the data-access layer, which fills the properties itself.
    private RenderJob()
    {
    }

    /// <summary>A job is queued as soon as it is made.</summary>
    public RenderJob(Guid id, Guid organizationId, Guid storyboardId, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        StoryboardId = storyboardId;
        State = RenderJobState.Queued;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The Storyboard version being rendered.</summary>
    public Guid StoryboardId { get; private set; }

    public RenderJobState State { get; private set; }

    /// <summary>Why the job failed, in words for the member. Only a failed job has one.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>What the job made. Only a completed job has one.</summary>
    public Guid? RenderedVideoId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the state last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Moves to a stage of the work. Ending the job is <see cref="Fail"/> or <see cref="Complete"/>.</summary>
    /// <returns>False, with nothing changed, when the job cannot go from its state to that one.</returns>
    public bool MoveTo(RenderJobState state, DateTimeOffset now) =>
        state is not (RenderJobState.Completed or RenderJobState.Failed) && Become(state, now);

    /// <returns>False, with nothing changed, when the job has already ended.</returns>
    public bool Fail(string reason, DateTimeOffset now)
    {
        if (!Become(RenderJobState.Failed, now)) return false;
        FailureReason = reason.Length > FailureReasonMaxLength ? reason[..FailureReasonMaxLength] : reason;
        return true;
    }

    /// <returns>False, with nothing changed, unless the job is in quality review.</returns>
    public bool Complete(Guid renderedVideoId, DateTimeOffset now)
    {
        if (!Become(RenderJobState.Completed, now)) return false;
        RenderedVideoId = renderedVideoId;
        return true;
    }

    private bool Become(RenderJobState state, DateTimeOffset now)
    {
        if (!State.CanBecome(state)) return false;
        State = state;
        UpdatedAt = now;
        return true;
    }
}

/// <summary>Where a render job is. The stages are in the order the work goes through them.</summary>
public enum RenderJobState
{
    Created,

    /// <summary>Waiting for the worker.</summary>
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
    /// generate; and from anything that has not ended, to failed or cancelled.
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
        (_, RenderJobState.Failed or RenderJobState.Cancelled) => !from.HasEnded(),
        _ => false,
    };

    /// <summary>Completed, failed or cancelled: nothing follows.</summary>
    public static bool HasEnded(this RenderJobState state) =>
        state is RenderJobState.Completed or RenderJobState.Failed or RenderJobState.Cancelled;
}
