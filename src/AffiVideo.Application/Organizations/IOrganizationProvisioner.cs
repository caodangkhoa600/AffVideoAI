namespace AffiVideo.Application.Organizations;

/// <summary>
/// Creates Organizations and sets what only the system sets on them. Sign-up is closed, so this is not reachable over HTTP:
/// the seed command uses it, and so do tests that need a second Organization.
/// </summary>
public interface IOrganizationProvisioner
{
    /// <summary>
    /// Creates an Organization with one Owner. Does nothing and returns null when a
    /// member with that email already exists, so it can be run again safely.
    /// </summary>
    Task<Guid?> CreateAsync(string name, string ownerEmail, string ownerPassword, CancellationToken cancellationToken);

    /// <summary>
    /// Gives an Organization the Affiliate Lab (ADR 0001). The flag is set here and
    /// nowhere else: no endpoint changes it.
    /// </summary>
    /// <returns>False, with nothing changed, when the Organization already has it.</returns>
    Task<bool> EnableAffiliateLabAsync(Guid organizationId, CancellationToken cancellationToken);
}

/// <summary>The Organization the seed command creates. Its Owner's credentials are in the README.</summary>
public static class DemonstrationOrganization
{
    public const string Name = "AffiVideo Demo";
    public const string OwnerEmail = "owner@demo.affivideo.local";
    public const string OwnerPassword = "demo-owner-password";
}
