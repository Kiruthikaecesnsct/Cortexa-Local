using Collector.Domain.Enums;
using Collector.Domain.Extraction;

namespace Collector.Application.Knowledge;

public sealed class UnitSplitter
{
    public (ExtractionUnit First, ExtractionUnit Second)? Split(ExtractionUnit unit)
    {
        var position = NearestBreakToMiddle(unit.Text);
        if (position < 0)
        {
            return null;
        }

        var firstText = unit.Text[..position];
        var secondText = unit.Text[(position + 1)..];
        if (string.IsNullOrWhiteSpace(firstText) || string.IsNullOrWhiteSpace(secondText))
        {
            return null;
        }

        var firstLines = firstText.Count(character => character == '\n') + 1;
        return (Half(unit, firstText, "a", 0, firstLines), Half(unit, secondText, "b", firstLines, null));
    }

    private static int NearestBreakToMiddle(string text)
    {
        var middle = text.Length / 2;
        var before = text.LastIndexOf('\n', middle);
        var after = text.IndexOf('\n', middle);
        if (before < 0 || (after >= 0 && after - middle < middle - before))
        {
            return after;
        }

        return before;
    }

    private static ExtractionUnit Half(ExtractionUnit unit, string text, string suffix, int lineOffset, int? lineCount)
    {
        var isFile = unit.UnitKind == UnitKind.File && unit.StartLine is not null;
        var start = isFile ? unit.StartLine + lineOffset : unit.StartLine;
        var end = isFile && lineCount is not null ? start + lineCount - 1 : unit.EndLine;
        return unit with
        {
            Id = $"{unit.Id}.{suffix}",
            Text = text,
            TokenCount = Math.Max(1, unit.TokenCount * text.Length / Math.Max(1, unit.Text.Length)),
            StartLine = start,
            EndLine = end,
        };
    }
}
