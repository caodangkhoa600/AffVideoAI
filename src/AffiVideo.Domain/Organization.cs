namespace AffiVideo.Domain;

/// <summary>The tenant that owns products, projects and videos.</summary>
public sealed class Organization
{
    public const int NameMaxLength = 100;

    public Organization(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public void Rename(string name) => Name = name;
}
