using System.Text.RegularExpressions;

namespace Collector.Infrastructure.Remote;

public static partial class LinkHeaderParser
{
    public static Uri? ParseNext(IEnumerable<string> headerValues, Uri requestUri)
    {
        foreach (var value in headerValues)
        {
            foreach (Match match in LinkPattern().Matches(value))
            {
                if (IsNext(match.Groups["params"].Value) && TryResolve(match.Groups["url"].Value, requestUri, out var next))
                {
                    return next;
                }
            }
        }

        return null;
    }

    private static bool IsNext(string parameters)
    {
        var rel = RelPattern().Match(parameters);
        return rel.Success
            && rel.Groups["rel"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("next", StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryResolve(string raw, Uri requestUri, out Uri next)
    {
        next = requestUri;
        if (!Uri.TryCreate(requestUri, raw, out var candidate))
        {
            return false;
        }

        next = candidate;
        return candidate.Scheme == requestUri.Scheme
            && string.Equals(candidate.Host, requestUri.Host, StringComparison.OrdinalIgnoreCase)
            && candidate.Port == requestUri.Port;
    }

    [GeneratedRegex("<(?<url>[^>]*)>(?<params>[^<]*)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"rel\s*=\s*""?(?<rel>[^"";,]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex RelPattern();
}
