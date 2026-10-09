using System.Text.RegularExpressions;
using Collector.Domain.Enums;

namespace Collector.Application.Remote;

public static partial class OrgUrlParser
{
    public static OrgUrl? Parse(SourceType provider, string? input)
    {
        var text = input?.Trim() ?? string.Empty;
        var matches = provider switch
        {
            SourceType.Github => GitHubPattern().IsMatch(text),
            SourceType.AzureDevops => AzureDevOpsPattern().IsMatch(text),
            _ => false,
        };
        return matches ? new OrgUrl(provider, LastSegment(text)) : null;
    }

    private static string LastSegment(string text)
    {
        var segments = text.TrimEnd('/').Split('/');
        return segments[^1];
    }

    [GeneratedRegex(@"^https://(www\.)?github\.com/(orgs/)?[A-Za-z0-9][A-Za-z0-9-]{0,38}/?$")]
    private static partial Regex GitHubPattern();

    [GeneratedRegex(@"^https://dev\.azure\.com/[A-Za-z0-9][A-Za-z0-9-]{0,48}[A-Za-z0-9]?/?$")]
    private static partial Regex AzureDevOpsPattern();
}
