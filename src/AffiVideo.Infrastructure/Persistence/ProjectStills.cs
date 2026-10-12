using AffiVideo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AffiVideo.Infrastructure.Persistence;

internal static class ProjectStills
{
    /// <summary>
    /// Holds the Project's row until the transaction ends. What a Project's generation may spend, which
    /// of its Scenes have a Still, and which Still a new Storyboard version refers to are read and then
    /// written. Two requests doing that at the same moment would each see what the other is about to
    /// change: the same Scene paid for twice, the ceiling passed between them, or a version made beside
    /// an approval that refers to nothing. So every such request holds the row while it decides: one
    /// decides at a time. Disposing without committing keeps nothing.
    /// </summary>
    /// <returns>Null when the Project was deleted after it was read.</returns>
    public static async Task<IDbContextTransaction?> HoldProjectAsync(
        this AffiVideoDbContext database, Guid projectId, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var held = await database.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Projects" WHERE "Id" = {projectId} FOR UPDATE""")
            .ToListAsync(cancellationToken);
        if (held.Count == 1) return transaction;
        await transaction.DisposeAsync();
        return null;
    }

    /// <summary>The Project's Stills that a new Storyboard version may refer to.</summary>
    public static Task<List<Still>> ApprovedStillsAsync(this AffiVideoDbContext database, Guid projectId, CancellationToken cancellationToken) =>
        database.Stills.AsNoTracking()
            .Where(still => still.ProjectId == projectId && still.State == StillState.Approved)
            .ToListAsync(cancellationToken);
}
