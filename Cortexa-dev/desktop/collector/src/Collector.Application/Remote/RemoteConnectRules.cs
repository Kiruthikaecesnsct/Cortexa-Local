using System.Globalization;
using System.Text.RegularExpressions;
using Collector.Domain.Enums;

namespace Collector.Application.Remote;

public static partial class RemoteConnectRules
{
    public const string GitHubUrlExample = "https://github.com/your-organization";
    public const string AzureDevOpsUrlExample = "https://dev.azure.com/your-organization";
    public const string TokenRequired = "Enter a personal access token";
    public const string HostRequired = "Enter the IP address or hostname";
    public const string PortInvalid = "Enter a port between 1 and 65535";
    public const string UsernameRequired = "Enter the SSH username";
    public const string KeyFileRequired = "Pick the SSH private key file";
    public const string FolderInvalid = "Enter an absolute path (e.g. /var/data) or start with ~";

    private const int MinPort = 1;
    private const int MaxPort = 65535;

    public static TokenFormResult ValidateTokenForm(SourceType provider, string? url, string? token) => new(
        OrgUrlParser.Parse(provider, url),
        OrganizationUrlError(provider, url),
        TokenError(token),
        token?.Trim() ?? string.Empty);

    public static SshFormErrors ValidateSshForm(SshFormInput input) => new(
        HostError(input.Host),
        PortError(input.Port),
        UsernameError(input.Username),
        KeyFileError(input.KeyFilePath),
        FolderError(input.Folder));

    public static string UrlExample(SourceType provider) =>
        provider == SourceType.AzureDevops ? AzureDevOpsUrlExample : GitHubUrlExample;

    public static string? OrganizationUrlError(SourceType provider, string? url) =>
        OrgUrlParser.Parse(provider, url) is null ? $"Enter a URL like {UrlExample(provider)}" : null;

    public static string? TokenError(string? token) => IsBlank(token) ? TokenRequired : null;

    public static string? HostError(string? host) => IsBlank(host) ? HostRequired : null;

    public static string? PortError(string? port) => TryParsePort(port, out _) ? null : PortInvalid;

    public static string? UsernameError(string? username) => IsBlank(username) ? UsernameRequired : null;

    public static string? KeyFileError(string? keyFilePath) => IsBlank(keyFilePath) ? KeyFileRequired : null;

    public static string? FolderError(string? folder) =>
        FolderPattern().IsMatch(folder?.Trim() ?? string.Empty) ? null : FolderInvalid;

    public static bool TryParsePort(string? text, out int port)
    {
        var parsed = int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port);
        return parsed && port is >= MinPort and <= MaxPort;
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    [GeneratedRegex(@"^(/|~(/.*)?)$|^/.*$")]
    private static partial Regex FolderPattern();
}
