namespace Cortexa.Identity.Domain.Entities;

public sealed class Organization
{
    private Organization() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public void SoftDelete()
    {
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Rename(string name)
    {
        Name = name;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static Organization Create(Guid id, string name)
    {
        var now = DateTimeOffset.UtcNow;

        return new Organization
        {
            Id = id,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
    }

    public static Organization Create(string name)
    {
        return Create(Guid.NewGuid(), name);
    }
}
