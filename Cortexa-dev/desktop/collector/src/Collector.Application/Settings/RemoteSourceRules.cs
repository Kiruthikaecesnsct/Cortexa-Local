using System.Text.RegularExpressions;

namespace Collector.Application.Settings;

public static partial class RemoteSourceRules
{
    public const string OrganizationInvalidReason =
        "Use letters, numbers and hyphens only, up to 50 characters.";

    public static bool IsValidOrganization(string? value) =>
        !string.IsNullOrEmpty(value) && OrganizationPattern().IsMatch(value);

    public static SettingsSaveResult Validate(RemoteSourceSettings settings)
    {
        var organization = settings.AzureDevOpsOrganization.Trim();
        return organization.Length == 0 || IsValidOrganization(organization)
            ? SettingsSaveResult.Ok
            : SettingsSaveResult.Invalid(EndpointField.AzureDevOpsOrganization, OrganizationInvalidReason);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,49}\z")]
    private static partial Regex OrganizationPattern();
}
