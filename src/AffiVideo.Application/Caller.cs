namespace AffiVideo.Application;

/// <summary>
/// Who the current unit of work (an HTTP request, a seed run, later a job) is
/// acting for. The data-access layer scopes every query to this Organization and
/// refuses writes for any other; until a caller is identified it sees nothing
/// that an Organization owns.
/// </summary>
public sealed class Caller
{
    public Guid? OrganizationId { get; private set; }

    /// <summary>Absent when the system itself is acting, as the seed does.</summary>
    public Guid? MemberId { get; private set; }

    public void Identify(Guid organizationId, Guid? memberId)
    {
        OrganizationId = organizationId;
        MemberId = memberId;
    }
}
