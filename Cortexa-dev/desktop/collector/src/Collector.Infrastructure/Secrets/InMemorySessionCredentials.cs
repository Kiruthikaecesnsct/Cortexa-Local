using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Infrastructure.Secrets;

public sealed class InMemorySessionCredentials : ISessionCredentials
{
    private readonly Lock _gate = new();
    private readonly Dictionary<SourceType, string> _tokens = [];
    private string? _passphrase;

    public void SetToken(SourceType source, string token)
    {
        lock (_gate)
        {
            _tokens[source] = token;
        }
    }

    public string? GetToken(SourceType source)
    {
        lock (_gate)
        {
            return _tokens.GetValueOrDefault(source);
        }
    }

    public void SetSshPassphrase(string passphrase)
    {
        lock (_gate)
        {
            _passphrase = passphrase;
        }
    }

    public string? GetSshPassphrase()
    {
        lock (_gate)
        {
            return _passphrase;
        }
    }

    public void Clear(SourceType source)
    {
        lock (_gate)
        {
            _tokens.Remove(source);
            if (source == SourceType.Ssh)
            {
                _passphrase = null;
            }
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            _tokens.Clear();
            _passphrase = null;
        }
    }
}
