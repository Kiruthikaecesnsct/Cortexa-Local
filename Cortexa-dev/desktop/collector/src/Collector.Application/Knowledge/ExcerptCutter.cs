namespace Collector.Application.Knowledge;

public sealed class ExcerptCutter
{
    private const int WindowFactor = 2;

    public string? Cut(string unitText, Anchor? anchor)
    {
        var start = anchor?.Offset ?? 0;
        var window = unitText.Substring(start, Math.Min(unitText.Length - start, UploadLimitsMirror.ExcerptMax * WindowFactor));
        var excerpt = UploadFieldClamp.Truncate(window, UploadLimitsMirror.ExcerptMax);
        return excerpt.Length == 0 ? null : excerpt;
    }
}
