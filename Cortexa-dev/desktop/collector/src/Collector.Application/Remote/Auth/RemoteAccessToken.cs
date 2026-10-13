namespace Collector.Application.Remote.Auth;

public enum RemoteTokenKind
{
    Pat,
    OAuth,
}

public sealed record RemoteAccessToken(string Value, RemoteTokenKind Kind)
{
    public override string ToString() => $"RemoteAccessToken {{ Kind = {Kind} }}";
}
