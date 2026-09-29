namespace Cortexa.Identity.Domain.Entities;

public sealed class RefreshToken
{
    private RefreshToken() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool Revoked { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive => UsedAt is null && !Revoked && ExpiresAt > DateTimeOffset.UtcNow;

    public void MarkUsed()
    {
        if (UsedAt is not null)
            return;
        UsedAt = DateTimeOffset.UtcNow;
    }

    public void Revoke() => Revoked = true;

    public static RefreshToken Create(Guid userId, string tokenHash, DateTimeOffset expiresAt)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            Revoked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
