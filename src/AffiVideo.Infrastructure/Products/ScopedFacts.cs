using AffiVideo.Application;
using AffiVideo.Application.Products;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Products;

// No method names an Organization: the context's filter leaves only the caller's
// Products and Facts. A change of state and its audit log entry are saved together,
// and the save fails if the Fact's state is no longer the one that was read.
internal sealed class ScopedFacts(AffiVideoDbContext database, Caller caller, TimeProvider clock) : IFacts
{
    private const string ChangedMeanwhile = "Someone else changed this Fact at the same moment. Try again.";

    public async Task<FactRecord?> AddAsync(Guid productId, FactDetails details, CancellationToken cancellationToken)
    {
        if (!await ProductExistsAsync(productId, cancellationToken)) return null;

        var fact = Propose(productId, details);
        await database.SaveChangesAsync(cancellationToken);
        return new FactRecord(fact, ConfirmedByEmail: null);
    }

    public async Task<Page<FactRecord>?> ListAsync(Guid productId, FactState? state, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await ProductExistsAsync(productId, cancellationToken)) return null;

        var all = database.Facts.AsNoTracking().Where(f => f.ProductId == productId);
        if (state is { } only)
        {
            all = all.Where(f => f.State == only);
        }

        var items = await WithConfirmer(all)
            .OrderBy(x => x.Fact.CreatedAt).ThenBy(x => x.Fact.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<FactRecord>(
            items.Select(x => new FactRecord(x.Fact, x.ConfirmedByEmail)).ToList(),
            page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<FactRecord?> FindAsync(Guid productId, Guid factId, CancellationToken cancellationToken) =>
        await WithConfirmer(database.Facts.AsNoTracking().Where(f => f.Id == factId && f.ProductId == productId))
            .SingleOrDefaultAsync(cancellationToken) is { } found
            ? new FactRecord(found.Fact, found.ConfirmedByEmail)
            : null;

    public async Task<FactChange?> ConfirmAsync(Guid productId, Guid factId, CancellationToken cancellationToken)
    {
        var fact = await TrackedAsync(productId, factId, cancellationToken);
        if (fact is null) return null;
        var member = CallingMember();

        var now = clock.GetUtcNow();
        if (!fact.Confirm(member, now))
        {
            return FactChange.Refuse(fact.State == FactState.Confirmed
                ? "This Fact is already Confirmed."
                : "This Fact is Withdrawn and cannot be Confirmed.");
        }
        Record(AuditActions.FactConfirmed, fact, member, now);
        if (!await SaveAsync(cancellationToken)) return FactChange.Refuse(ChangedMeanwhile);

        return FactChange.Made((await FindAsync(productId, factId, cancellationToken))!);
    }

    public async Task<FactChange?> WithdrawAsync(Guid productId, Guid factId, CancellationToken cancellationToken)
    {
        var fact = await TrackedAsync(productId, factId, cancellationToken);
        if (fact is null) return null;

        if (!Withdraw(fact)) return FactChange.Refuse("This Fact is already Withdrawn.");
        if (!await SaveAsync(cancellationToken)) return FactChange.Refuse(ChangedMeanwhile);

        return FactChange.Made((await FindAsync(productId, factId, cancellationToken))!);
    }

    public async Task<FactChange?> ReplaceAsync(Guid productId, Guid factId, FactDetails details, CancellationToken cancellationToken)
    {
        var fact = await TrackedAsync(productId, factId, cancellationToken);
        if (fact is null) return null;

        if (!Withdraw(fact)) return FactChange.Refuse("This Fact is already Withdrawn. Add a new Fact instead.");
        var replacement = Propose(productId, details);
        // One save: the old Fact is Withdrawn and the new one added, or neither.
        if (!await SaveAsync(cancellationToken)) return FactChange.Refuse(ChangedMeanwhile);

        return FactChange.Made(new FactRecord(replacement, ConfirmedByEmail: null));
    }

    private Fact Propose(Guid productId, FactDetails details)
    {
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("A Fact is added for the caller's Organization, and there is no caller.");

        var fact = new Fact(Guid.CreateVersion7(), organizationId, productId, details, clock.GetUtcNow());
        database.Facts.Add(fact);
        return fact;
    }

    private bool Withdraw(Fact fact)
    {
        var member = CallingMember();
        var now = clock.GetUtcNow();
        if (!fact.Withdraw(now)) return false;

        Record(AuditActions.FactWithdrawn, fact, member, now);
        return true;
    }

    private void Record(string action, Fact fact, Guid member, DateTimeOffset now) =>
        database.AuditLog.Add(new AuditLogEntry(Guid.CreateVersion7(), fact.OrganizationId, member, action, fact.Id, now));

    private Guid CallingMember() =>
        caller.MemberId ?? throw new InvalidOperationException("Only a member can Confirm or Withdraw a Fact.");

    /// <returns>False, with nothing saved, when the Fact's state was changed by someone else after it was read.</returns>
    private async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        database.Products.AnyAsync(p => p.Id == productId, cancellationToken);

    // The Fact has to be this Product's: an identifier from one Product does not work under another.
    private Task<Fact?> TrackedAsync(Guid productId, Guid factId, CancellationToken cancellationToken) =>
        database.Facts.SingleOrDefaultAsync(f => f.Id == factId && f.ProductId == productId, cancellationToken);

    private IQueryable<FactWithConfirmer> WithConfirmer(IQueryable<Fact> facts) =>
        from fact in facts
        join member in database.Members on fact.ConfirmedByMemberId equals (Guid?)member.Id into confirmers
        from confirmer in confirmers.DefaultIfEmpty()
        select new FactWithConfirmer { Fact = fact, ConfirmedByEmail = confirmer.Email };

    private sealed class FactWithConfirmer
    {
        public required Fact Fact { get; init; }

        public required string? ConfirmedByEmail { get; init; }
    }
}
