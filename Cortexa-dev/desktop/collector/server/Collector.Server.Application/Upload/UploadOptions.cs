namespace Collector.Server.Application.Upload;

public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    public string Engine { get; set; } = string.Empty;

    public long MaxRequestBytes { get; set; }

    public int MaxDocuments { get; set; }

    public int MaxItemsPerDocument { get; set; }

    public int MaxBatchNameLength { get; set; }

    public int MaxTitleLength { get; set; }

    public int MaxSummaryLength { get; set; }

    public int MaxDetailsLength { get; set; }

    public int MaxIdempotencyKeyLength { get; set; }

    public ModelDefaultsOptions ModelDefaults { get; set; } = new();
}
