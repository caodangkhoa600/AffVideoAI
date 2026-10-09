namespace AffiVideo.Domain;

/// <summary>
/// One creative treatment of a Project: a creative template and a Hook. Neither
/// ever changes, and nor does the identifier, so that results stay attached to
/// the creative idea they were measured for. A different Hook is a different Variant.
/// </summary>
public sealed class Variant : IOwnedByOrganization
{
    public const int HookMaxLength = 200;

    // For the data-access layer, which fills the properties itself.
    private Variant()
    {
    }

    public Variant(Guid id, Guid organizationId, Guid projectId, CreativeTemplate creativeTemplate, string hook, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProjectId = projectId;
        CreativeTemplate = creativeTemplate;
        Hook = hook.Trim();
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public CreativeTemplate CreativeTemplate { get; private set; }

    /// <summary>The opening line, meant to stop the viewer scrolling.</summary>
    public string Hook { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether this is the Hook the Variant already has, however it is spaced or capitalised.</summary>
    public bool HasHook(string hook) => string.Equals(Hook, hook.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A separate Variant of the same Project with the same creative template and another Hook.</summary>
    public Variant DuplicateWith(Guid id, string hook, DateTimeOffset now) =>
        new(id, OrganizationId, ProjectId, CreativeTemplate, hook, now);
}

/// <summary>How a Variant looks and reads: its Scene structure, layout, typography and motion.</summary>
public enum CreativeTemplate
{
    LuxuryCinematic,
    ProductShowcase,
    ProblemSolution,
}
