using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface ISessionCredentials
{
    void SetToken(SourceType source, string token);

    string? GetToken(SourceType source);

    void SetSshPassphrase(string passphrase);

    string? GetSshPassphrase();

    void Clear(SourceType source);

    void ClearAll();
}
