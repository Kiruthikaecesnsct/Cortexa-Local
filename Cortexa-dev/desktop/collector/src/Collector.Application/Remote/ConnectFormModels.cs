namespace Collector.Application.Remote;

public sealed record TokenFormResult(OrgUrl? Organization, string? UrlError, string? TokenError, string Token)
{
    public bool IsValid => Organization is not null && TokenError is null;
}

public sealed record SshFormInput(string? Host, string? Port, string? Username, string? KeyFilePath, string? Folder);

public sealed record SshFormErrors(
    string? HostError,
    string? PortError,
    string? UsernameError,
    string? KeyFileError,
    string? FolderError)
{
    public bool IsValid => HostError is null
        && PortError is null
        && UsernameError is null
        && KeyFileError is null
        && FolderError is null;
}
