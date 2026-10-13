using AffiVideo.Domain;

namespace AffiVideo.Application.Generation;

/// <summary>
/// Paid generation for the caller's Organization's Storyboards: what a provider makes
/// for a video before it is rendered (ADR 0003). A Project, a Variant, a Still or a Clip of
/// another Organization is answered exactly as one that does not exist. Nothing here
/// generates: what is confirmed waits for the worker.
/// </summary>
public interface IGeneration
{
    /// <summary>What generation for this Storyboard version would make and what that is estimated to cost. It spends nothing.</summary>
    /// <returns>Null when the Variant has no such version.</returns>
    Task<GenerationEstimate?> EstimateAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken);

    /// <summary>
    /// Queues the Stills, Clips and speech of the estimate, once the member has confirmed its total, unless some were
    /// already queued with this idempotency key: then those are the answer, whatever state they are in.
    /// A Clip whose Still is not approved yet waits for it, and is queued by that approval.
    /// </summary>
    /// <param name="confirmedTotal">The total of the estimate the member saw. Nothing is queued unless it is the estimate's now.</param>
    /// <returns>Null when the Variant has no such version.</returns>
    Task<GenerationConfirmation?> ConfirmAsync(
        Guid projectId, Guid variantId, int version, decimal confirmedTotal, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The Stills made for the Project, newest first, rejected and failed ones included. Null when there is no such Project.</summary>
    Task<Page<StillRecord>?> ListStillsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);

    Task<StillRecord?> FindStillAsync(Guid stillId, CancellationToken cancellationToken);

    /// <summary>
    /// Records that the caller accepts a Still that is ready for review: it may then be used. A Clip that
    /// was confirmed with it and waits for it is queued, to be made from it.
    /// </summary>
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

    /// <summary>The Clips made for the Project, newest first, failed ones and those that wait for their Still included. Null when there is no such Project.</summary>
    Task<Page<ClipRecord>?> ListClipsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);

    Task<ClipRecord?> FindClipAsync(Guid clipId, CancellationToken cancellationToken);

    /// <summary>Records that the caller accepts a Clip that is ready for review, having watched it: it may then be used.</summary>
    /// <returns>Null when there is no such Clip.</returns>
    Task<ClipDecision?> ApproveClipAsync(Guid clipId, CancellationToken cancellationToken);

    /// <summary>The video of a Clip that has been made, for the caller to dispose.</summary>
    /// <returns>Null when there is no such Clip, or it has no video: it is not made yet, or never was.</returns>
    Task<Stream?> OpenClipAsync(Guid clipId, CancellationToken cancellationToken);

    /// <summary>The speech made for the Project, newest first, failed pieces included. Null when there is no such Project.</summary>
    Task<Page<SpeechRecord>?> ListSpeechAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);

    Task<SpeechRecord?> FindSpeechAsync(Guid speechId, CancellationToken cancellationToken);

    /// <summary>The sound of speech that has been made, for the caller to dispose.</summary>
    /// <returns>Null when there is no such speech, or it has no sound: it is not made yet, or never was.</returns>
    Task<Stream?> OpenSpeechAsync(Guid speechId, CancellationToken cancellationToken);
}

/// <param name="Cost">The speech's production cost record. Null until it is made or has failed for good.</param>
public sealed record SpeechRecord(Speech Speech, GenerationCostRecord? Cost);

/// <param name="Cost">The Clip's production cost record. Null until it is made or has failed for good.</param>
public sealed record ClipRecord(Clip Clip, GenerationCostRecord? Cost);

/// <param name="Cost">The Still's production cost record. Null until it is made or has failed for good.</param>
public sealed record StillRecord(Still Still, GenerationCostRecord? Cost);

/// <param name="Unavailable">Why nothing can be generated, in words for the member. Null when it can.</param>
/// <param name="Stills">The Stills confirming would make: one for each generated Scene that has none yet.</param>
/// <param name="Clips">The Clips confirming would make: one for each Scene of generated footage that has none yet.</param>
/// <param name="Speech">The speech confirming would make: one piece for each narration text of the version that has none yet.</param>
/// <param name="Total">What the Stills, the Clips and the speech are estimated to cost together.</param>
/// <param name="Resumed">
/// What Clips confirmed before come to, that wait for the Stills of this estimate and are not counted as spent
/// while no Still is on its way for them. Confirming puts them on their way again, so it counts toward the ceiling.
/// </param>
/// <param name="Spent">What generation for the Project is estimated to have cost so far, rejected takes and what is on its way included.</param>
/// <param name="Ceiling">The most the Project may spend on generation.</param>
/// <param name="ImageProvider">What would make the Stills.</param>
/// <param name="VideoProvider">What would make the Clips.</param>
/// <param name="SpeechProvider">What would speak the narration text.</param>
/// <param name="Voice">The voice it would be spoken in: the Project's.</param>
/// <param name="SceneStills">The Still each generated Scene of the version has, for those that have one.</param>
/// <param name="SceneClips">The Clip each Scene of generated footage of the version has, for those that have one.</param>
/// <param name="SceneSpeech">The speech each Scene of the version has, for those that have some.</param>
/// <param name="AnotherStillAmount">What asking for another Still would be estimated to cost now. Null when no Still can be generated.</param>
public sealed record GenerationEstimate(
    string? Unavailable, IReadOnlyList<EstimatedStill> Stills, IReadOnlyList<EstimatedClip> Clips, IReadOnlyList<EstimatedSpeech> Speech,
    decimal Total, decimal Resumed, string Currency, decimal Spent, decimal Ceiling, ImageProvider ImageProvider, VideoProvider VideoProvider,
    SpeechProvider SpeechProvider, string Voice, IReadOnlyList<SceneStill> SceneStills,
    IReadOnlyList<SceneClip> SceneClips, IReadOnlyList<SceneSpeech> SceneSpeech, decimal? AnotherStillAmount);

/// <param name="ScenePosition">Of the Scene the Still is for, in this Storyboard version.</param>
/// <param name="FactIds">The Facts the Scene cites, which the Still rests on.</param>
public sealed record EstimatedStill(int ScenePosition, Technique Technique, string Description, decimal EstimatedAmount, IReadOnlyList<Guid> FactIds);

/// <param name="ScenePosition">Of the Scene the Clip is for, in this Storyboard version.</param>
/// <param name="DurationMs">How long a Clip is asked for: the Scene's length.</param>
/// <param name="FactIds">The Facts the Scene cites, which the Clip rests on.</param>
/// <param name="StillId">The approved Still it would be made from at once. Null when it would wait for a Still to be approved.</param>
public sealed record EstimatedClip(
    int ScenePosition, string Description, int DurationMs, decimal EstimatedAmount, IReadOnlyList<Guid> FactIds, Guid? StillId);

/// <param name="ScenePosition">Of the Scene the speech is for, in this Storyboard version.</param>
/// <param name="NarrationText">What is said: the Scene's narration text.</param>
/// <param name="EstimatedDurationMs">How long the text is estimated to take to say, before any of it is spoken.</param>
/// <param name="Warning">Why the text looks too long for its Scene, in words for the member. Null when it looks as if it fits.</param>
public sealed record EstimatedSpeech(int ScenePosition, string NarrationText, decimal EstimatedAmount, int EstimatedDurationMs, string? Warning);

/// <param name="ScenePosition">Of the Scene in this Storyboard version.</param>
/// <param name="Still">The Project's Still of the picture the Scene describes: on its way, waiting for the member, or approved.</param>
public sealed record SceneStill(int ScenePosition, StillRecord Still);

/// <param name="ScenePosition">Of the Scene in this Storyboard version.</param>
/// <param name="Clip">The Project's Clip of the picture the Scene describes: waiting for its Still, on its way, waiting for the member, or approved.</param>
public sealed record SceneClip(int ScenePosition, ClipRecord Clip);

/// <param name="ScenePosition">Of the Scene in this Storyboard version.</param>
/// <param name="Speech">The Project's speech of the Scene's narration text: on its way, or made.</param>
public sealed record SceneSpeech(int ScenePosition, SpeechRecord Speech);

/// <summary>Either the Clip as the member's decision left it, or the reason it was not made, in words for the member.</summary>
public sealed record ClipDecision(ClipRecord? Clip, string? Refused)
{
    public static ClipDecision Refuse(string reason) => new(null, reason);
}

/// <summary>Either the Still as the member's decision left it or the one queued in its place, or the reason for neither, in words for the member.</summary>
/// <param name="Queued">False when the Still was queued by an earlier request with the same idempotency key.</param>
public sealed record StillDecision(StillRecord? Still, bool Queued, string? Refused)
{
    public static StillDecision Refuse(string reason) => new(null, false, reason);
}

/// <summary>Either the Stills, Clips and speech, or the reason nothing was queued, in words for the member.</summary>
/// <param name="Queued">False when they were queued by an earlier request with the same idempotency key.</param>
public sealed record GenerationConfirmation(
    IReadOnlyList<StillRecord> Stills, IReadOnlyList<ClipRecord> Clips, IReadOnlyList<SpeechRecord> Speech, bool Queued, string? Refused)
{
    public static GenerationConfirmation Refuse(string reason) => new([], [], [], false, reason);
}
