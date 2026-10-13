namespace Collector.Application.Remote.Auth;

public sealed record OAuthTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? AccessExpiresAt,
    DateTimeOffset? RefreshExpiresAt)
{
    public bool NeedsRefresh(DateTimeOffset now, TimeSpan skew) =>
        AccessExpiresAt is { } expiry && now + skew >= expiry;

    public bool CanRefresh(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(RefreshToken) && (RefreshExpiresAt is not { } expiry || now < expiry);

    public override string ToString() => "OAuthTokenSet { }";
}
