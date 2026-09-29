namespace Cortexa.Identity.Domain.Entities;

public sealed class Permission
{
    private Permission() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; private set; }

    public static Permission Create(string name, string description) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = description,
        CreatedDate = DateTimeOffset.UtcNow
    };
}
