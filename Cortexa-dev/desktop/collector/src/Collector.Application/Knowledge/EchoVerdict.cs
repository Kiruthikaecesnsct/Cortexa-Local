namespace Collector.Application.Knowledge;

public sealed record EchoVerdict(bool IsEcho, string? Reason)
{
    public static EchoVerdict Clean { get; } = new(false, null);
}

public static class EchoReasons
{
    public const string TitleIsIdentifier = "title_is_identifier";

    public const string TitleIdentifierShare = "title_identifier_share";

    public const string SummaryIdentifierShare = "summary_identifier_share";

    public const string SummaryCopied = "summary_copied";
}
