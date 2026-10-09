using AffiVideo.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AffiVideo.Infrastructure.Persistence;

/// <summary>
/// Which work is Flagged for Review, read from what is so now: a Scene's record
/// of a Fact it used, that Fact being Withdrawn, and nobody having cleared the
/// flag. Nothing is written when a Fact is withdrawn, so no work is ever missed.
/// </summary>
internal static class ReviewFlags
{
    /// <summary>What a member is told when others kept clearing flags on the same work as fast as they were read.</summary>
    public const string ClearedMeanwhile = "Someone else cleared a flag here at the same moment. Try again.";

    /// <summary>What a member is told when none of the flags they reviewed is on the work any longer, and others are.</summary>
    public const string FlagsChanged = "The flags here have changed since you looked. Look again, then clear what you have reviewed.";

    /// <summary>One row for each use of a Withdrawn Fact by a Storyboard version, unless its flag was cleared.</summary>
    public static IQueryable<Raised> OnStoryboards(this AffiVideoDbContext database) =>
        from storyboard in database.Storyboards
        from scene in storyboard.Scenes
        from used in scene.Facts
        join fact in database.Facts on used.FactId equals fact.Id
        where fact.State == FactState.Withdrawn
            && !database.ClearedFlags.Any(cleared => cleared.StoryboardId == storyboard.Id && cleared.FactId == used.FactId)
        select new Raised
        {
            SubjectId = storyboard.Id, FactId = used.FactId, Text = used.Text, WithdrawnAt = fact.WithdrawnAt,
            ScenePosition = scene.Position, FactPosition = used.Position,
        };

    /// <summary>
    /// The same for Rendered Videos, each of which used what its Storyboard version
    /// used. A video's flag is its own: clearing the version's does not clear it.
    /// </summary>
    public static IQueryable<Raised> OnRenderedVideos(this AffiVideoDbContext database) =>
        from video in database.RenderedVideos
        join storyboard in database.Storyboards on video.StoryboardId equals storyboard.Id
        from scene in storyboard.Scenes
        from used in scene.Facts
        join fact in database.Facts on used.FactId equals fact.Id
        where fact.State == FactState.Withdrawn
            && !database.ClearedFlags.Any(cleared => cleared.RenderedVideoId == video.Id && cleared.FactId == used.FactId)
        select new Raised
        {
            SubjectId = video.Id, FactId = used.FactId, Text = used.Text, WithdrawnAt = fact.WithdrawnAt,
            ScenePosition = scene.Position, FactPosition = used.Position,
        };

    /// <summary>The flags on each of these, in the order the work uses the Facts. Work that is not flagged is left out.</summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReviewFlag>>> OfAsync(
        this IQueryable<Raised> raised, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken)
    {
        if (subjectIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<ReviewFlag>>();

        var rows = await raised.Where(row => subjectIds.Contains(row.SubjectId)).ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.SubjectId)
            .ToDictionary(
                subject => subject.Key,
                IReadOnlyList<ReviewFlag> (subject) => subject
                    .OrderBy(row => row.ScenePosition).ThenBy(row => row.FactPosition)
                    // A Fact two Scenes used is one flag.
                    .DistinctBy(row => row.FactId)
                    .Select(row => new ReviewFlag(row.FactId, row.Text, row.WithdrawnAt ?? default))
                    .ToList());
    }

    /// <summary>The flags on one Storyboard version or Rendered Video. Empty when it is not flagged.</summary>
    public static async Task<IReadOnlyList<ReviewFlag>> OfAsync(this IQueryable<Raised> raised, Guid subjectId, CancellationToken cancellationToken) =>
        (await raised.OfAsync([subjectId], cancellationToken)).GetValueOrDefault(subjectId, []);

    /// <summary>Saves the clearing of a flag with whatever else is waiting to be saved.</summary>
    /// <returns>
    /// False, with nothing saved, when another member cleared the same flag at the same
    /// moment, or the work it was on was deleted.
    /// </returns>
    public static async Task<bool> SaveUnlessClearedMeanwhileAsync(this AffiVideoDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation,
        })
        {
            database.ChangeTracker.Clear();
            return false;
        }
    }

    internal sealed class Raised
    {
        /// <summary>The Storyboard version or the Rendered Video.</summary>
        public required Guid SubjectId { get; init; }

        public required Guid FactId { get; init; }

        public required string Text { get; init; }

        public required DateTimeOffset? WithdrawnAt { get; init; }

        public required int ScenePosition { get; init; }

        public required int FactPosition { get; init; }
    }
}
