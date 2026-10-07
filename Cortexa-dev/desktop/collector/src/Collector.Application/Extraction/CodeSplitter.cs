using System.Text.RegularExpressions;
using Collector.Application.Ports;

namespace Collector.Application.Extraction;

public sealed record CodeUnit
{
    public required string Text { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }
}

public sealed partial class CodeSplitter(ITokenCounter tokenCounter)
{
    private const int FallbackWindowTokens = 2000;

    public IReadOnlyList<CodeUnit> Split(string text, CodeLanguageInfo language)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var boundaries = FindBoundaries(lines, language.Strategy);
        var units = BuildUnitsFromBoundaries(lines, boundaries);
        return units.SelectMany(unit => ApplyTokenWindowFallback(unit)).ToList();
    }

    private static List<int> FindBoundaries(string[] lines, CodeSplitStrategy strategy)
    {
        var boundaries = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsBoundaryLine(lines[i], strategy))
            {
                boundaries.Add(i);
            }
        }

        return boundaries;
    }

    private static bool IsBoundaryLine(string line, CodeSplitStrategy strategy) => strategy switch
    {
        CodeSplitStrategy.Brace => IsBraceUnitStart(line),
        CodeSplitStrategy.Indentation => IndentationUnitStart().IsMatch(line),
        _ => false,
    };

    private static bool IsBraceUnitStart(string line) =>
        BraceClassStart().IsMatch(line) ||
        BraceMethodStart().IsMatch(line) ||
        BraceConstructorStart().IsMatch(line) ||
        BraceFunctionKeywordStart().IsMatch(line);

    private static List<CodeUnit> BuildUnitsFromBoundaries(string[] lines, List<int> boundaries)
    {
        if (boundaries.Count == 0)
        {
            return [new CodeUnit { Text = string.Join('\n', lines), StartLine = 1, EndLine = lines.Length }];
        }

        var units = new List<CodeUnit>();
        if (boundaries[0] > 0)
        {
            units.Add(SliceUnit(lines, 0, boundaries[0] - 1));
        }

        for (var i = 0; i < boundaries.Count; i++)
        {
            var start = boundaries[i];
            var end = i + 1 < boundaries.Count ? boundaries[i + 1] - 1 : lines.Length - 1;
            units.Add(SliceUnit(lines, start, end));
        }

        return units;
    }

    private static CodeUnit SliceUnit(string[] lines, int startIndex, int endIndex) => new()
    {
        Text = string.Join('\n', lines.Skip(startIndex).Take(endIndex - startIndex + 1)),
        StartLine = startIndex + 1,
        EndLine = endIndex + 1,
    };

    private IEnumerable<CodeUnit> ApplyTokenWindowFallback(CodeUnit unit)
    {
        if (tokenCounter.Count(unit.Text) <= FallbackWindowTokens)
        {
            yield return unit;
            yield break;
        }

        var lines = unit.Text.Split('\n');
        var windowStart = 0;
        var windowTokens = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var lineTokens = tokenCounter.Count(lines[i]) + 1;
            if (windowTokens > 0 && windowTokens + lineTokens > FallbackWindowTokens)
            {
                yield return OffsetWindow(lines, windowStart, i - 1, unit.StartLine);
                windowStart = i;
                windowTokens = 0;
            }

            windowTokens += lineTokens;
        }

        yield return OffsetWindow(lines, windowStart, lines.Length - 1, unit.StartLine);
    }

    private static CodeUnit OffsetWindow(string[] lines, int startIndex, int endIndex, int unitStartLine)
    {
        var window = SliceUnit(lines, startIndex, endIndex);
        var lineOffset = unitStartLine - 1;
        return window with { StartLine = window.StartLine + lineOffset, EndLine = window.EndLine + lineOffset };
    }

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|static|sealed|abstract|virtual|override|async|unsafe|partial|readonly|extern|export|default|final|pub)\s+)*(?:class|interface|struct|enum|record|trait|impl)\s+[A-Za-z_]\w*")]
    private static partial Regex BraceClassStart();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|sealed|abstract|virtual|override|async|unsafe|partial|extern|export|default|pub|readonly|new)\s+)+[\w<>\[\],\.\?:&*]+\s+[A-Za-z_]\w*\s*(?:<[^>]*>)?\s*\([^)]*\)")]
    private static partial Regex BraceMethodStart();

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|static)\s+)+[A-Z]\w*\s*\([^)]*\)\s*(?:\{|:)?\s*$")]
    private static partial Regex BraceConstructorStart();

    [GeneratedRegex(@"^\s*(?:pub\s+)?(?:async\s+)?(?:fn|func|function)\s+[A-Za-z_]\w*")]
    private static partial Regex BraceFunctionKeywordStart();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+[A-Za-z_]\w*|^\s*class\s+[A-Za-z_]\w*")]
    private static partial Regex IndentationUnitStart();
}
