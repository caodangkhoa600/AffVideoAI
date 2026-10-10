using AffiVideo.Domain;

namespace AffiVideo.Application.Rendering;

/// <summary>
/// The Rendered Videos of the caller's Organization: the library. A Rendered Video
/// of another Organization is answered exactly as one that does not exist.
/// </summary>
public interface IRenderedVideos
{
    Task<Page<RenderedVideoRecord>> ListAsync(RenderedVideoFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<RenderedVideoRecord?> FindAsync(Guid videoId, CancellationToken cancellationToken);

    /// <summary>The MP4 to preview, in either state, for the caller to dispose. Null when there is no such Rendered Video.</summary>
    Task<Stream?> OpenPreviewAsync(Guid videoId, CancellationToken cancellationToken);

    /// <summary>The MP4 to save as a file, which only an approved Rendered Video gives.</summary>
    /// <returns>Null when there is no such Rendered Video.</returns>
    Task<RenderedVideoDownload?> DownloadAsync(Guid videoId, CancellationToken cancellationToken);

    /// <summary>Approves a Rendered Video that is ready for review in the calling member's name, and records it in the audit log.</summary>
    /// <returns>Null when there is no such Rendered Video.</returns>
    Task<RenderedVideoChange?> ApproveAsync(Guid videoId, CancellationToken cancellationToken);

    /// <summary>
    /// Clears the flags these Withdrawn Facts put on a Rendered Video, in the calling
    /// member's name, and records it in the audit log. Nothing else about it changes.
    /// </summary>
    /// <param name="factIds">The Facts whose flags the member has reviewed.</param>
    /// <returns>Null when there is no such Rendered Video.</returns>
    Task<RenderedVideoChange?> ClearFlagAsync(Guid videoId, IReadOnlyCollection<Guid> factIds, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a Rendered Video in either state, with its file, and records it in the
    /// audit log. The job that made it is kept, with nothing to show for it. One
    /// that has a Published Post is kept: that is the record of where it is live.
    /// </summary>
    Task<RenderedVideoDeletion> DeleteAsync(Guid videoId, CancellationToken cancellationToken);
}

/// <summary>What narrows the library, and its order. Whatever is left out narrows nothing.</summary>
/// <param name="Search">Looked for in the Product's name and in the Project's objective, which is what a Project is called by.</param>
/// <param name="Flagged">True for those Flagged for Review only, false for the others only.</param>
public sealed record RenderedVideoFilter(
    string? Search, RenderedVideoState? State, CreativeTemplate? CreativeTemplate, bool? Flagged, RenderedVideoOrder Order);

/// <summary>The order of the library, by when each Rendered Video was rendered.</summary>
public enum RenderedVideoOrder
{
    NewestFirst,
    OldestFirst,
}

/// <summary>A Rendered Video with what it was made from, as those are now.</summary>
/// <param name="ProjectObjective">What the Project is called by.</param>
/// <param name="StoryboardVersion">The version of the Variant's Storyboard that was rendered.</param>
/// <param name="ApprovedByEmail">Of the member who approved it, when one did.</param>
/// <param name="Flags">One for each Withdrawn Fact the video used, until a member clears it. Empty when it is not Flagged for Review.</param>
/// <param name="Costs">
/// The production cost records of the job that made the video, by attempt: the one that
/// made it and any that failed before it. Empty for a video rendered before costs were recorded.
/// </param>
public sealed record RenderedVideoRecord(
    RenderedVideo Video,
    Guid ProductId,
    string ProductName,
    Guid ProjectId,
    string ProjectObjective,
    Guid VariantId,
    CreativeTemplate CreativeTemplate,
    string Hook,
    int StoryboardVersion,
    string? ApprovedByEmail,
    IReadOnlyList<ReviewFlag> Flags,
    IReadOnlyList<ProductionCostRecord> Costs);

public enum RenderedVideoDeletion
{
    Deleted,

    /// <summary>There is no such Rendered Video.</summary>
    NotFound,

    /// <summary>Nothing was deleted: a Published Post names the Rendered Video.</summary>
    HasPublishedPost,
}

/// <summary>Either the MP4, for the caller to dispose, or the reason it may not be downloaded, in words for the member.</summary>
public sealed record RenderedVideoDownload(RenderedVideoRecord Record, Stream? Content, string? Refused);

/// <summary>Either the Rendered Video as it now is, or the reason it was not changed, in words for the member.</summary>
public sealed record RenderedVideoChange(RenderedVideoRecord? Record, string? Refused)
{
    public static RenderedVideoChange Made(RenderedVideoRecord record) => new(record, null);

    public static RenderedVideoChange Refuse(string reason) => new(null, reason);
}
