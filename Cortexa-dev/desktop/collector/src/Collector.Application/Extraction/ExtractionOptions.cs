namespace Collector.Application.Extraction;

public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    public int MaxParallelSplits { get; set; } = 4;
}
