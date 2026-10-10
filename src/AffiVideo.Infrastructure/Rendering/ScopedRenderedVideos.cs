using AffiVideo.Application;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AffiVideo.Infrastructure.Rendering;

// No method names an Organization: the context's filter leaves only the caller's
// Rendered Videos, and a file is only ever looked up by the key of a Rendered
// Video found that way. A change and its audit log entry are saved together, and
// the save fails if the video's state is no longer the one that was read.
internal sealed class ScopedRenderedVideos(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    ILogger<ScopedRenderedVideos> logger) : IRenderedVideos
{
    private const string AlreadyApproved = "This Rendered Video is already approved.";
    private const string NotFlagged = "This Rendered Video is not Flagged for Review.";

    public async Task<Page<RenderedVideoRecord>> ListAsync(RenderedVideoFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var all = InContext(database.RenderedVideos.AsNoTracking());
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var contains = LikePattern.Containing(filter.Search);
            all = all.Where(x =>
                EF.Functions.ILike(x.ProductName, contains, LikePattern.Escape) ||
                EF.Functions.ILike(x.ProjectObjective, contains, LikePattern.Escape));
        }
        if (filter.State is { } state)
        {
            all = all.Where(x => x.Video.State == state);
        }
        if (filter.CreativeTemplate is { } template)
        {
            all = all.Where(x => x.CreativeTemplate == template);
        }
        if (filter.Flagged is { } flagged)
        {
            var raised = database.OnRenderedVideos();
            all = all.Where(x => raised.Any(flag => flag.SubjectId == x.Video.Id) == flagged);
        }

        var ordered = filter.Order == RenderedVideoOrder.OldestFirst
            ? all.OrderBy(x => x.Video.CreatedAt).ThenBy(x => x.Video.Id)
            : all.OrderByDescending(x => x.Video.CreatedAt).ThenByDescending(x => x.Video.Id);
        var items = await ordered.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        var flags = await database.OnRenderedVideos().OfAsync(items.Select(x => x.Video.Id).ToList(), cancellationToken);
        var costs = await CostsOfAsync(items.Select(x => x.Video).ToList(), cancellationToken);
        return new Page<RenderedVideoRecord>(
            items.Select(x => ToRecord(x, flags.GetValueOrDefault(x.Video.Id, []), costs[x.Video.RenderJobId].ToList())).ToList(),
            page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<RenderedVideoRecord?> FindAsync(Guid videoId, CancellationToken cancellationToken) =>
        await InContext(database.RenderedVideos.AsNoTracking().Where(v => v.Id == videoId))
            .SingleOrDefaultAsync(cancellationToken) is { } found
            ? ToRecord(
                found, await database.OnRenderedVideos().OfAsync(videoId, cancellationToken),
                (await CostsOfAsync([found.Video], cancellationToken))[found.Video.RenderJobId].ToList())
            : null;

    // What each video's job cost, attempt by attempt: the attempts that failed before the one that made the video are the job's too.
    private async Task<ILookup<Guid, ProductionCostRecord>> CostsOfAsync(
        IReadOnlyCollection<RenderedVideo> videos, CancellationToken cancellationToken)
    {
        var jobIds = videos.Select(video => (Guid?)video.RenderJobId).ToList();
        var records = await database.ProductionCostRecords.AsNoTracking()
            .Where(cost => jobIds.Contains(cost.RenderJobId))
            .OrderBy(cost => cost.Attempt)
            .ToListAsync(cancellationToken);
        return records.ToLookup(cost => cost.RenderJobId!.Value);
    }

    public async Task<Stream?> OpenPreviewAsync(Guid videoId, CancellationToken cancellationToken) =>
        await database.RenderedVideos.AsNoTracking().SingleOrDefaultAsync(v => v.Id == videoId, cancellationToken) is { } video
            ? await OpenFileAsync(video, cancellationToken)
            : null;

    public async Task<RenderedVideoDownload?> DownloadAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(videoId, cancellationToken);
        if (found is null) return null;
        if (!found.Video.CanBeDownloaded)
        {
            return new RenderedVideoDownload(found, null, "This Rendered Video has not been approved. Approve it, then download it.");
        }

        return await OpenFileAsync(found.Video, cancellationToken) is { } content
            ? new RenderedVideoDownload(found, content, null)
            : null;
    }

    private async Task<Stream?> OpenFileAsync(RenderedVideo video, CancellationToken cancellationToken)
    {
        var content = await storage.OpenAsync(video.StorageKey, cancellationToken);
        if (content is null)
        {
            logger.LogError("The file of Rendered Video {VideoId} is missing from storage at {StorageKey}", video.Id, video.StorageKey);
        }
        return content;
    }

    public async Task<RenderedVideoChange?> ApproveAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var video = await database.RenderedVideos.SingleOrDefaultAsync(v => v.Id == videoId, cancellationToken);
        if (video is null) return null;
        var member = CallingMember();

        var now = clock.GetUtcNow();
        if (!video.Approve(member, now)) return RenderedVideoChange.Refuse(AlreadyApproved);
        Record(AuditActions.RenderedVideoApproved, video, member, now);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Approved or deleted by someone else after it was read. Nothing of this attempt is saved.
            database.ChangeTracker.Clear();
            return await FindAsync(videoId, cancellationToken) is null
                ? null
                : RenderedVideoChange.Refuse(AlreadyApproved);
        }

        return await FindAsync(videoId, cancellationToken) is { } approved ? RenderedVideoChange.Made(approved) : null;
    }

    public async Task<RenderedVideoChange?> ClearFlagAsync(Guid videoId, IReadOnlyCollection<Guid> factIds, CancellationToken cancellationToken)
    {
        var member = CallingMember();
        // Someone may clear one of the flags, or delete the video, between their being read and cleared here; then they are read again.
        for (var attempt = 1; ; attempt++)
        {
            if (await FindAsync(videoId, cancellationToken) is not { } found) return null;
            if (found.Flags.Count == 0) return RenderedVideoChange.Refuse(NotFlagged);
            // Only what the member saw is cleared: a Fact withdrawn since is a flag nobody has reviewed.
            var reviewed = found.Flags.Where(flag => factIds.Contains(flag.FactId)).ToList();
            if (reviewed.Count == 0) return RenderedVideoChange.Refuse(ReviewFlags.FlagsChanged);

            var now = clock.GetUtcNow();
            database.ClearedFlags.AddRange(reviewed.Select(flag => ClearedFlag.OnRenderedVideo(found.Video, flag.FactId, member, now)));
            Record(AuditActions.RenderedVideoFlagCleared, found.Video, member, now);
            // The clearing and its audit log entry are saved together, or neither is.
            if (await database.SaveUnlessClearedMeanwhileAsync(cancellationToken))
            {
                return RenderedVideoChange.Made(found with { Flags = await database.OnRenderedVideos().OfAsync(videoId, cancellationToken) });
            }
            if (attempt == 3) return RenderedVideoChange.Refuse(ReviewFlags.ClearedMeanwhile);
        }
    }

    public async Task<RenderedVideoDeletion> DeleteAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var member = CallingMember();
        // Someone may approve the video between its being read and deleted here; then it is read again.
        for (var attempt = 1; ; attempt++)
        {
            var video = await database.RenderedVideos.SingleOrDefaultAsync(v => v.Id == videoId, cancellationToken);
            if (video is null) return RenderedVideoDeletion.NotFound;
            if (await database.PublishedPosts.AnyAsync(post => post.RenderedVideoId == videoId, cancellationToken))
            {
                return RenderedVideoDeletion.HasPublishedPost;
            }

            database.RenderedVideos.Remove(video);
            var job = await database.RenderJobs.SingleOrDefaultAsync(j => j.Id == video.RenderJobId, cancellationToken);
            job?.ForgetRenderedVideo();
            Record(AuditActions.RenderedVideoDeleted, video, member, clock.GetUtcNow());
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 5)
            {
                database.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                // A Published Post was recorded for it after the check above. Nothing of this attempt is saved.
                return RenderedVideoDeletion.HasPublishedPost;
            }

            // The record is what makes the file reachable, and it is gone. A file left
            // behind is wasted space, not a reason to fail the request, and it is
            // deleted even if the member has stopped waiting.
            try
            {
                await storage.DeleteAsync(video.StorageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", video.StorageKey);
            }
            return RenderedVideoDeletion.Deleted;
        }
    }

    private void Record(string action, RenderedVideo video, Guid member, DateTimeOffset now) =>
        database.AuditLog.Add(new AuditLogEntry(Guid.CreateVersion7(), video.OrganizationId, member, action, video.Id, now));

    private Guid CallingMember() =>
        caller.MemberId ?? throw new InvalidOperationException("Only a member can approve, review or delete a Rendered Video.");

    // A Rendered Video is kept with its Storyboard, and so with the Variant, Project
    // and Product it was made from: every one of them is there to be joined.
    private IQueryable<VideoInContext> InContext(IQueryable<RenderedVideo> videos) =>
        from video in videos
        join storyboard in database.Storyboards on video.StoryboardId equals storyboard.Id
        join variant in database.Variants on storyboard.VariantId equals variant.Id
        join project in database.Projects on variant.ProjectId equals project.Id
        join product in database.Products on project.ProductId equals product.Id
        join member in database.Members on video.ApprovedByMemberId equals (Guid?)member.Id into approvers
        from approver in approvers.DefaultIfEmpty()
        select new VideoInContext
        {
            Video = video,
            ProductId = product.Id,
            ProductName = product.Name,
            ProjectId = project.Id,
            ProjectObjective = project.Objective,
            VariantId = variant.Id,
            CreativeTemplate = variant.CreativeTemplate,
            Hook = variant.Hook,
            StoryboardVersion = storyboard.Version,
            ApprovedByEmail = approver.Email,
        };

    private static RenderedVideoRecord ToRecord(
        VideoInContext found, IReadOnlyList<ReviewFlag> flags, IReadOnlyList<ProductionCostRecord> costs) => new(
        found.Video, found.ProductId, found.ProductName, found.ProjectId, found.ProjectObjective,
        found.VariantId, found.CreativeTemplate, found.Hook, found.StoryboardVersion, found.ApprovedByEmail, flags, costs);

    private sealed class VideoInContext
    {
        public required RenderedVideo Video { get; init; }

        public required Guid ProductId { get; init; }

        public required string ProductName { get; init; }

        public required Guid ProjectId { get; init; }

        public required string ProjectObjective { get; init; }

        public required Guid VariantId { get; init; }

        public required CreativeTemplate CreativeTemplate { get; init; }

        public required string Hook { get; init; }

        public required int StoryboardVersion { get; init; }

        public required string? ApprovedByEmail { get; init; }
    }
}
