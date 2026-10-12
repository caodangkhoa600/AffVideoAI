using AffiVideo.Domain;

namespace AffiVideo.Application.Generation;

/// <summary>
/// Paid generation for the caller's Organization's Storyboards: what a provider makes
/// for a video before it is rendered (ADR 0003). A Project, a Variant or a Still of
/// another Organization is answered exactly as one that does not exist. Nothing here
/// generates: what is confirmed waits for the worker.
/// </summary>
public interface IGeneration
{
    /// <summary>What generation for this Storyboard version would make and what that is estimated to cost. It spends nothing.</summary>
    /// <returns>Null when the Variant has no such version.</returns>
    Task<GenerationEstimate?> EstimateAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken);

    /// <summary>
    /// Queues the Stills of the estimate, once the member has confirmed its total, unless Stills were
    /// already queued with this idempotency key: then those are the answer, whatever state they are in.
    /// </summary>
    /// <param name="confirmedTotal">The total of the estimate the member saw. Nothing is queued unless it is the estimate's now.</param>
    /// <returns>Null when the Variant has no such version.</returns>
    Task<GenerationConfirmation?> ConfirmAsync(
        Guid projectId, Guid variantId, int version, decimal confirmedTotal, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The Stills made for the Project, newest first, rejected and failed ones included. Null when there is no such Project.</summary>
    Task<Page<StillRecord>?> ListStillsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);

    Task<StillRecord?> FindStillAsync(Guid stillId, CancellationToken cancellationToken);

    /// <summary>Records that the caller accepts a Still that is ready for review: it may then be used.</summary>
    /// <returns>Null when there is no such Still.</returns>
    Task<StillDecision?> ApproveStillAsync(Guid stillId, CancellationToken cancellationToken);

    /// <summary>
    /// Rejects a Still that has been made and queues another of the same picture in its place: a new paid
    /// generation, at the amount the member confirms. The rejected one is kept, with what it cost. A request
    /// repeated with the same idempotency key is answered with the Still the first one queued.
    /// </summary>
    /// <returns>Null when there is no such Still.</returns>
    /// <param name="confirmedAmount">What the member saw another would cost. Nothing is queued unless that is what it costs now.</param>
    Task<StillDecision?> AnotherStillAsync(Guid stillId, decimal confirmedAmount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The picture of a Still that has been made, for the caller to dispose.</summary>
    /// <returns>Null when there is no such Still, or it has no picture: it is not made yet, or never was.</returns>
    Task<Stream?> OpenStillAsync(Guid stillId, CancellationToken cancellationToken);
}

/// <param name="Cost">The Still's production cost record. Null until it is made or has failed for good.</param>
public sealed record StillRecord(Still Still, GenerationCostRecord? Cost);

/// <param name="Unavailable">Why nothing can be generated, in words for the member. Null when it can.</param>
/// <param name="Stills">The Stills confirming would make: one for each generated Scene that has none yet.</param>
/// <param name="Total">What they are estimated to cost together.</param>
/// <param name="Spent">What generation for the Project is estimated to have cost so far, rejected takes and what is on its way included.</param>
/// <param name="Ceiling">The most the Project may spend on generation.</param>
/// <param name="ImageProvider">What would make the Stills.</param>
/// <param name="SceneStills">The Still each generated Scene of the version has, for those that have one.</param>
/// <param name="AnotherStillAmount">What asking for another Still would be estimated to cost now. Null when nothing can be generated.</param>
public sealed record GenerationEstimate(
    string? Unavailable, IReadOnlyList<EstimatedStill> Stills, decimal Total, string Currency, decimal Spent, decimal Ceiling,
    ImageProvider ImageProvider, IReadOnlyList<SceneStill> SceneStills, decimal? AnotherStillAmount);

/// <param name="ScenePosition">Of the Scene the Still is for, in this Storyboard version.</param>
public sealed record EstimatedStill(int ScenePosition, Technique Technique, string Description, decimal EstimatedAmount);

/// <param name="ScenePosition">Of the Scene in this Storyboard version.</param>
/// <param name="Still">The Project's Still of the picture the Scene describes: on its way, waiting for the member, or approved.</param>
public sealed record SceneStill(int ScenePosition, StillRecord Still);

/// <summary>Either the Still as the member's decision left it or the one queued in its place, or the reason for neither, in words for the member.</summary>
/// <param name="Queued">False when the Still was queued by an earlier request with the same idempotency key.</param>
public sealed record StillDecision(StillRecord? Still, bool Queued, string? Refused)
{
    public static StillDecision Refuse(string reason) => new(null, false, reason);
}

/// <summary>Either the Stills, or the reason nothing was queued, in words for the member.</summary>
/// <param name="Queued">False when the Stills were queued by an earlier request with the same idempotency key.</param>
public sealed record GenerationConfirmation(IReadOnlyList<StillRecord> Stills, bool Queued, string? Refused)
{
    public static GenerationConfirmation Refuse(string reason) => new([], false, reason);
}
