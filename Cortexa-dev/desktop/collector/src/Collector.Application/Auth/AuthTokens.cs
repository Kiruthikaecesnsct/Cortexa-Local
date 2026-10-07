using System.Text;

namespace Collector.Application.Auth;

public sealed record AuthTokens(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ExpiresAt = ").Append(ExpiresAt);
        return true;
    }
}
