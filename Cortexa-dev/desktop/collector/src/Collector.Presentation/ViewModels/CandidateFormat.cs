using System.Globalization;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public static class CandidateFormat
{
    private const double MinScore = 0;
    private const double MaxScore = 100;
    private const string HarvestingEngine = "harvesting";
    private const string SeedingEngine = "seeding";

    public static string? Score(double? value) =>
        value is { } score && !double.IsNaN(score)
            ? Math.Round(Math.Clamp(score, MinScore, MaxScore), 0, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture)
            : null;

    public static string? Patentability(int? value) =>
        value is { } rating
            ? Math.Clamp(rating, (int)MinScore, (int)MaxScore).ToString(CultureInfo.InvariantCulture)
            : null;

    public static string Kind(string raw)
    {
        var words = raw.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = string.Join(' ', words);
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();
    }

    public static bool IsHarvesting(string engine) =>
        string.Equals(engine, HarvestingEngine, StringComparison.OrdinalIgnoreCase);

    public static string Engine(string engine)
    {
        if (IsHarvesting(engine))
        {
            return HistoryStrings.EngineHarvesting;
        }

        return string.Equals(engine, SeedingEngine, StringComparison.OrdinalIgnoreCase)
            ? HistoryStrings.EngineSeeding
            : Kind(engine);
    }
}
