using Collector.Server.Application.Upload;

namespace Collector.Server.Tests.Upload;

internal static class UploadTestOptions
{
    public const string ConfiguredEngine = "DUAL";

    public static UploadOptions Create(Action<UploadOptions>? configure = null)
    {
        var options = new UploadOptions
        {
            Engine = ConfiguredEngine,
            MaxRequestBytes = 1_048_576,
            MaxDocuments = 50,
            MaxItemsPerDocument = 500,
            MaxBatchNameLength = 200,
            MaxTitleLength = 300,
            MaxSummaryLength = 2000,
            MaxDetailsLength = 8000,
            MaxIdempotencyKeyLength = 128,
            ModelDefaults = new ModelDefaultsOptions
            {
                ExtractionModel = "default-extraction",
                PrimaryEvidenceModel = "default-evidence",
                ScoringModel = "default-scoring",
                SeedingModel = "default-seeding",
                SeedingMode = "default-mode"
            }
        };
        configure?.Invoke(options);
        return options;
    }
}
