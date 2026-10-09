namespace Collector.Application.Settings;

public sealed record SshConnectionProfile(
    string Host,
    int Port,
    string Username,
    string KeyFilePath,
    string RemoteRoot);
