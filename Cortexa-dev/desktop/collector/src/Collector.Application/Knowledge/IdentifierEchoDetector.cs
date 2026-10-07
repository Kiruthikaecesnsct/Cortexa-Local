using System.Text;
using System.Text.RegularExpressions;

namespace Collector.Application.Knowledge;

public sealed partial class IdentifierEchoDetector
{
    public const double TitleEchoRatio = 0.5;

    public const double SummaryEchoRatio = 0.3;

    public const double SummaryCopyRatio = 0.6;

    public const int MinCopyCheckLength = 40;

    private const string CallSuffix = "()";
    private const char Underscore = '_';
    private const char PathSeparator = '.';
    private const int MinPathSegmentLength = 2;
    private const string LeadingTrim = "\"'`([{<“‘";
    private const string TrailingTrim = "\"'`,;:!?.]}>”’";

    public EchoVerdict Evaluate(string title, string summary, string unitText)
    {
        var titleTokens = Tokenize(title);
        var titleEchoes = CountEchoes(titleTokens, unitText);
        if (titleTokens.Count == 1 && titleEchoes == 1)
        {
            return Echo(EchoReasons.TitleIsIdentifier);
        }

        if (Exceeds(titleEchoes, titleTokens.Count, TitleEchoRatio))
        {
            return Echo(EchoReasons.TitleIdentifierShare);
        }

        var summaryTokens = Tokenize(summary);
        if (Exceeds(CountEchoes(summaryTokens, unitText), summaryTokens.Count, SummaryEchoRatio))
        {
            return Echo(EchoReasons.SummaryIdentifierShare);
        }

        return IsCopied(summary, unitText) ? Echo(EchoReasons.SummaryCopied) : EchoVerdict.Clean;
    }

    private static EchoVerdict Echo(string reason) => new(true, reason);

    private static bool Exceeds(int count, int total, double ratio) => total > 0 && (double)count / total > ratio;

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = TrimToken(raw);
            if (token.Any(char.IsLetterOrDigit))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static string TrimToken(string raw)
    {
        var token = raw.TrimStart(LeadingTrim.ToCharArray());
        while (token.Length > 0 && IsTrimmableTail(token))
        {
            token = token[..^1];
        }

        return token;
    }

    private static bool IsTrimmableTail(string token)
    {
        var last = token[^1];
        return TrailingTrim.Contains(last) || (last == ')' && !token.EndsWith(CallSuffix, StringComparison.Ordinal));
    }

    private static int CountEchoes(List<string> tokens, string unitText) =>
        tokens.Count(token => IsIdentifierShaped(token) && IsPresent(token, unitText));

    private static bool IsIdentifierShaped(string token) =>
        IsCall(token) || HasCamelHump(token) || IsSnake(token) || IsDotPath(token);

    private static bool IsCall(string token) =>
        token.EndsWith(CallSuffix, StringComparison.Ordinal)
        && token.Length > CallSuffix.Length
        && char.IsLetter(token[^(CallSuffix.Length + 1)]);

    private static bool HasCamelHump(string token)
    {
        for (var index = 0; index + 1 < token.Length; index++)
        {
            if (char.IsLower(token[index]) && char.IsUpper(token[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSnake(string token) => token.Contains(Underscore) && token.Any(char.IsLetter);

    private static bool IsDotPath(string token) =>
        DotPath().Matches(token).Any(match =>
            match.Value.Split(PathSeparator).Any(segment => segment.Length >= MinPathSegmentLength));

    private static bool IsPresent(string token, string unitText)
    {
        var bare = token.EndsWith(CallSuffix, StringComparison.Ordinal) ? token[..^CallSuffix.Length] : token;
        if (unitText.Contains(bare, StringComparison.Ordinal))
        {
            return true;
        }

        return IsDotPath(token)
            && bare.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .All(segment => unitText.Contains(segment, StringComparison.Ordinal));
    }

    private static bool IsCopied(string summary, string unitText)
    {
        var normalizedSummary = Normalize(summary);
        if (normalizedSummary.Length < MinCopyCheckLength)
        {
            return false;
        }

        var longest = new SuffixAutomaton(normalizedSummary).LongestMatchIn(Normalize(unitText));
        return (double)longest / normalizedSummary.Length > SummaryCopyRatio;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+")]
    private static partial Regex DotPath();
}
