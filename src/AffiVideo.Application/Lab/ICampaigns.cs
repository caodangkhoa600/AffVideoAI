using AffiVideo.Domain;

namespace AffiVideo.Application.Lab;

/// <summary>
/// The Campaigns of the caller's Organization, and the Variants they group. A
/// Campaign or a Variant of another Organization is answered exactly as one that
/// does not exist. Nothing here changes or removes a Variant.
/// </summary>
public interface ICampaigns
{
    Task<CampaignRecord> CreateAsync(string name, CancellationToken cancellationToken);

    Task<CampaignRecord?> FindAsync(Guid campaignId, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<Page<CampaignRecord>> ListAsync(CampaignStatus? status, PageRequest page, CancellationToken cancellationToken);

    Task<CampaignRecord?> RenameAsync(Guid campaignId, string name, CancellationToken cancellationToken);

    /// <summary>Archiving a Campaign that is already archived changes nothing.</summary>
    Task<CampaignRecord?> ArchiveAsync(Guid campaignId, CancellationToken cancellationToken);

    /// <summary>In the order they were added. Null when there is no such Campaign.</summary>
    Task<Page<CampaignVariantRecord>?> ListVariantsAsync(Guid campaignId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Adds a Variant to a Campaign. Adding one that is already in it changes nothing.</summary>
    /// <returns>False when there is no such Campaign or no such Variant.</returns>
    Task<bool> AddVariantAsync(Guid campaignId, Guid variantId, CancellationToken cancellationToken);

    /// <summary>Takes a Variant out of a Campaign. The Variant itself is kept.</summary>
    /// <returns>False when the Campaign does not group that Variant.</returns>
    Task<bool> RemoveVariantAsync(Guid campaignId, Guid variantId, CancellationToken cancellationToken);
}

/// <param name="VariantCount">How many Variants the Campaign groups.</param>
public sealed record CampaignRecord(Campaign Campaign, int VariantCount);

/// <summary>A Variant a Campaign groups, with what it belongs to, as those are now.</summary>
public sealed record CampaignVariantRecord(Variant Variant, Guid ProductId, string ProductName, DateTimeOffset AddedAt);
