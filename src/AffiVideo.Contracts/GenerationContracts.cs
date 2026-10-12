using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>
/// What generation for a Storyboard version would make and what that is estimated to cost,
/// for the member to see before confirming. Asking for it spends nothing.
/// </summary>
/// <param name="Available">Whether anything can be generated at all.</param>
/// <param name="Unavailable">Why not, in words for the member. Null when it can.</param>
/// <param name="Stills">The Stills confirming would make: one for each generated Scene that has none yet.</param>
/// <param name="Total">What they are estimated to cost together. An estimate from configured rates, never an amount anyone was billed.</param>
/// <param name="Spent">What generation for the Project is estimated to have cost so far, rejected takes and what is on its way included.</param>
/// <param name="Ceiling">The most the Project may spend on generation. A generation that would pass it is refused.</param>
/// <param name="ImageProvider">What would make the Stills. <c>Fake</c> returns a fixed picture and calls no provider.</param>
/// <param name="SceneStills">The Still each generated Scene of the version has, for those that have one.</param>
/// <param name="AnotherStillAmount">What asking for another Still would be estimated to cost now: the amount to confirm when asking. Null when nothing can be generated.</param>
public sealed record GenerationEstimateResponse(
    bool Available,
    string? Unavailable,
    IReadOnlyList<EstimatedStillResponse> Stills,
    decimal Total,
    string Currency,
    decimal Spent,
    decimal Ceiling,
    ImageProvider ImageProvider,
    IReadOnlyList<SceneStillResponse> SceneStills,
    decimal? AnotherStillAmount);

/// <param name="ScenePosition">Of the Scene the Still is for, in this Storyboard version.</param>
/// <param name="Description">What the Still is to show, as the provider is told.</param>
public sealed record EstimatedStillResponse(int ScenePosition, Technique Technique, string Description, decimal EstimatedAmount);

/// <param name="ScenePosition">Of the Scene in this Storyboard version.</param>
/// <param name="Still">The Project's Still of the picture the Scene describes. Every Variant of the Project has the same one.</param>
public sealed record SceneStillResponse(int ScenePosition, StillResponse Still);

/// <summary>The member's confirmation of an estimate. Without it nothing is generated.</summary>
/// <param name="ConfirmedTotal">The total of the estimate the member saw. It is refused unless it is the estimate's now.</param>
public sealed record ConfirmGenerationRequest(decimal ConfirmedTotal);

/// <summary>The member's confirmation of what another Still costs. Without it none is generated.</summary>
/// <param name="ConfirmedAmount">The amount the member saw. It is refused unless it is what another Still is estimated at now.</param>
public sealed record AnotherStillRequest(decimal ConfirmedAmount);

/// <param name="Stills">What the confirmation queued, for the worker to have made.</param>
public sealed record ConfirmedGenerationResponse(IReadOnlyList<StillResponse> Stills);

/// <summary>A generated image that a member approves before it is used.</summary>
/// <param name="ProjectId">The Project it was made for. Null once that Project has been deleted.</param>
/// <param name="Description">What it is to show, as the provider was told.</param>
/// <param name="State">Its state is all the progress a member is shown.</param>
/// <param name="Provider">What makes it.</param>
/// <param name="Model">The provider's model that made it. Null until it is made.</param>
/// <param name="Fake">Whether a fake provider makes it: it is then a fixed picture of nothing, and no provider was called.</param>
/// <param name="EstimatedAmount">What making it is estimated to cost. Nobody was billed this.</param>
/// <param name="Attempt">How many times a worker has taken it.</param>
/// <param name="RetryAt">When it will next be tried, while it waits after a failure that was not charged for.</param>
/// <param name="Failure">Why it was not made. Null unless it failed.</param>
/// <param name="Cost">Its Production cost record. Null until it is made or has failed for good.</param>
public sealed record StillResponse(
    Guid Id,
    Guid? ProjectId,
    Technique Technique,
    string Description,
    StillState State,
    ImageProvider Provider,
    string? Model,
    bool Fake,
    decimal EstimatedAmount,
    string Currency,
    int Attempt,
    DateTimeOffset? RetryAt,
    StillFailureResponse? Failure,
    StillCostResponse? Cost,
    Guid? ApprovedByMemberId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>What making a Still is estimated to have cost. It is kept whatever becomes of the Still, a rejected one included.</summary>
/// <param name="EstimatedAmount">An estimate from the rates in force as the Still was asked for, never an amount anyone was billed. Zero for an attempt the provider did not charge for.</param>
/// <param name="Model">The provider's model. Null when nothing was made.</param>
/// <param name="RatesVersion">Names the rates the amount was estimated with.</param>
public sealed record StillCostResponse(
    decimal EstimatedAmount, string Currency, ImageProvider Provider, string? Model, string RatesVersion, DateTimeOffset RecordedAt);

/// <param name="Kind">What the member can do about it: change the description of a picture that was refused, or ask again.</param>
/// <param name="Message">In words for the member.</param>
/// <param name="Billed">Whether the provider is taken to have charged for the attempt all the same.</param>
public sealed record StillFailureResponse(StillFailureKind Kind, string Message, bool Billed);
