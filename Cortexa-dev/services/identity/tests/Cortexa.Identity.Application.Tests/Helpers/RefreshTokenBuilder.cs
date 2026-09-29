using System.Reflection;
using Cortexa.Identity.Domain.Entities;

namespace Cortexa.Identity.Application.Tests.Helpers;

internal static class RefreshTokenBuilder
{
    internal static RefreshToken Build(
        Guid? id = null,
        Guid? userId = null,
        string tokenHash = "mock-hash-abc123",
        DateTimeOffset? expiresAt = null,
        bool revoked = false,
        DateTimeOffset? usedAt = null,
        DateTimeOffset? createdAt = null)
    {
        var token = (RefreshToken)Activator.CreateInstance(typeof(RefreshToken), nonPublic: true)!;

        SetProperty(token, nameof(RefreshToken.Id), id ?? Guid.NewGuid());
        SetProperty(token, nameof(RefreshToken.UserId), userId ?? Guid.NewGuid());
        SetProperty(token, nameof(RefreshToken.TokenHash), tokenHash);
        SetProperty(token, nameof(RefreshToken.ExpiresAt), expiresAt ?? DateTimeOffset.UtcNow.AddDays(7));
        SetProperty(token, nameof(RefreshToken.Revoked), revoked);
        SetProperty(token, nameof(RefreshToken.UsedAt), usedAt);
        SetProperty(token, nameof(RefreshToken.CreatedAt), createdAt ?? DateTimeOffset.UtcNow);

        return token;
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = typeof(RefreshToken).GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

        var backingField = typeof(RefreshToken).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (backingField is not null)
            backingField.SetValue(target, value);
        else
            property.SetValue(target, value);
    }
}
