using AffiVideo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Identity;

/// <summary>
/// The identity system's access to members. It is the one place that looks
/// across Organizations: signing in has to find a member by email before anyone
/// knows which Organization they belong to, and an email may be used only once
/// anywhere. Everything else reads members through the scoped
/// <see cref="AffiVideoDbContext.Members"/>.
/// </summary>
internal sealed class MemberStore(AffiVideoDbContext context, IdentityErrorDescriber describer)
    : UserOnlyStore<Member, AffiVideoDbContext, Guid>(context, describer)
{
    public override IQueryable<Member> Users => base.Users.IgnoreQueryFilters();

    // The base implementation uses Find, which applies the Organization filter.
    public override Task<Member?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var id = ConvertIdFromString(userId);
        return Users.SingleOrDefaultAsync(member => member.Id == id, cancellationToken);
    }
}
