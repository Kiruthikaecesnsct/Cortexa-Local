using System.Text;
using System.Text.RegularExpressions;

namespace Collector.Application.Knowledge;

public sealed partial class UntrustedSourceGuard(KnowledgePrompt prompt)
{
    private readonly GuardText _guard = prompt.Guard;

    public string SecurityText => string.Join(_guard.LineSeparator, _guard.SecurityLines);

    public string NeutralizeProse(string text)
    {
        var collapsed = WhitespaceRun().Replace(text, " ").Trim();
        var defused = DefuseEscapes(collapsed);
        return _guard.ProseSubstitutions.Aggregate(
            defused,
            (current, substitution) => current.Replace(substitution.From, substitution.To, StringComparison.Ordinal));
    }

    public string NeutralizeCode(string text)
    {
        var lines = DefuseEscapes(text).Split('\n');
        return string.Join('\n', lines.Select(DefuseHeaderMarker));
    }

    public string Wrap(string neutralizedText) =>
        string.Join(_guard.LineSeparator, _guard.Header, _guard.BeginMarker, neutralizedText, _guard.EndMarker);

    private string DefuseEscapes(string text) =>
        _guard.EscapeTokens.Aggregate(
            text,
            (current, token) => current.Replace(token, BreakToken(token), StringComparison.Ordinal));

    private string DefuseHeaderMarker(string line)
    {
        var indent = line.Length - line.TrimStart().Length;
        foreach (var marker in _guard.HeaderMarkers)
        {
            if (string.CompareOrdinal(line, indent, marker, 0, marker.Length) == 0)
            {
                return string.Concat(line.AsSpan(0, indent), BreakToken(marker), line.AsSpan(indent + marker.Length));
            }
        }

        return line;
    }

    private string BreakToken(string token)
    {
        var builder = new StringBuilder(token.Length * 2);
        for (var index = 0; index < token.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(_guard.ZeroWidthSpace);
            }

            builder.Append(token[index]);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
