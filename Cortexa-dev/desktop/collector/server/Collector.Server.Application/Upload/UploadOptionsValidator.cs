using Microsoft.Extensions.Options;

namespace Collector.Server.Application.Upload;

public sealed class UploadOptionsValidator : IValidateOptions<UploadOptions>
{
    private static readonly string[] Engines = ["harvesting", "seeding", "dual"];

    public ValidateOptionsResult Validate(string? name, UploadOptions options)
    {
        var failures = new List<string>();
        CheckEngine(options.Engine, failures);
        CheckCaps(options, failures);
        CheckModelDefaults(options.ModelDefaults, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckEngine(string engine, List<string> failures)
    {
        if (!Engines.Contains(engine, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add($"{Path(nameof(UploadOptions.Engine))} must be one of: {string.Join(", ", Engines)}.");
        }
    }

    private static void CheckCaps(UploadOptions options, List<string> failures)
    {
        CheckPositive(options.MaxRequestBytes, nameof(UploadOptions.MaxRequestBytes), failures);
        CheckPositive(options.MaxDocuments, nameof(UploadOptions.MaxDocuments), failures);
        CheckPositive(options.MaxItemsPerDocument, nameof(UploadOptions.MaxItemsPerDocument), failures);
        CheckPositive(options.MaxBatchNameLength, nameof(UploadOptions.MaxBatchNameLength), failures);
        CheckPositive(options.MaxTitleLength, nameof(UploadOptions.MaxTitleLength), failures);
        CheckPositive(options.MaxSummaryLength, nameof(UploadOptions.MaxSummaryLength), failures);
        CheckPositive(options.MaxDetailsLength, nameof(UploadOptions.MaxDetailsLength), failures);
        CheckPositive(options.MaxIdempotencyKeyLength, nameof(UploadOptions.MaxIdempotencyKeyLength), failures);
    }

    private static void CheckModelDefaults(ModelDefaultsOptions defaults, List<string> failures)
    {
        CheckText(defaults.ExtractionModel, nameof(ModelDefaultsOptions.ExtractionModel), failures);
        CheckText(defaults.PrimaryEvidenceModel, nameof(ModelDefaultsOptions.PrimaryEvidenceModel), failures);
        CheckText(defaults.ScoringModel, nameof(ModelDefaultsOptions.ScoringModel), failures);
        CheckText(defaults.SeedingModel, nameof(ModelDefaultsOptions.SeedingModel), failures);
        CheckText(defaults.SeedingMode, nameof(ModelDefaultsOptions.SeedingMode), failures);
    }

    private static void CheckPositive(long value, string name, List<string> failures)
    {
        if (value <= 0)
        {
            failures.Add($"{Path(name)} must be greater than zero.");
        }
    }

    private static void CheckText(string value, string name, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{Path(nameof(UploadOptions.ModelDefaults))}:{name} is required.");
        }
    }

    private static string Path(string name) => $"{UploadOptions.SectionName}:{name}";
}
