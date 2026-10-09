namespace AffiVideo.Domain;

/// <summary>
/// A record that belongs to one Organization. The data-access layer only returns
/// such a record to a caller from that Organization, and refuses to write one
/// for any other.
/// </summary>
public interface IOwnedByOrganization
{
    Guid OrganizationId { get; }
}
