using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure.Options;

public sealed class CosmosOptionsValidator : IValidateOptions<CosmosOptions>
{
    public ValidateOptionsResult Validate(string? name, CosmosOptions options)
    {
        var failures = new OptionFailures();
        failures.RequireAbsoluteUri(options.Endpoint, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.Endpoint)}");
        failures.RequireText(options.Database, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.Database)}");
        failures.RequireText(options.DocumentsContainer, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.DocumentsContainer)}");
        failures.RequireText(options.ChunksContainer, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.ChunksContainer)}");
        failures.RequireText(options.ProvenanceMapsContainer, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.ProvenanceMapsContainer)}");
        failures.RequireText(options.BatchesContainer, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.BatchesContainer)}");
        failures.RequireText(options.ConfigContainer, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.ConfigContainer)}");
        failures.RequireText(options.ReportsContainer, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.ReportsContainer)}");
        failures.RequirePositive(options.MaxConcurrentWrites, $"{CosmosOptions.SectionName}:{nameof(CosmosOptions.MaxConcurrentWrites)}");
        return failures.ToResult();
    }
}
