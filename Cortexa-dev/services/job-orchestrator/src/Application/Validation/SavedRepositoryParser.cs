using System.Text.Json;
using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Validation;

public sealed record SavedRepositoryParseResult(IReadOnlyList<SavedRepositoryRef> Repositories, string? Error)
{
    public static SavedRepositoryParseResult Fail(string error) => new([], error);
}

// Parses the repeated "saved_repositories" form field: one JSON object per saved branch.
// The ingestion service re-validates names with each provider's own rules before it
// reads storage; this check rejects malformed input at the edge.
public static class SavedRepositoryParser
{
    private const int MaxOwnerLength = 64;
    private const int MaxRepositoryLength = 129;
    private const int MaxBranchLength = 255;
    private const string GitHub = "github";
    private const string AzureDevOps = "azure-devops";
    private const string Malformed = "Each saved repository must be a JSON object with provider, owner, repository and branch.";

    public static SavedRepositoryParseResult Parse(IReadOnlyList<string?> rawValues, int maxCount)
    {
        if (rawValues.Count > maxCount)
            return SavedRepositoryParseResult.Fail($"At most {maxCount} saved repositories can be analyzed in one batch.");

        var parsed = new List<SavedRepositoryRef>(rawValues.Count);
        foreach (var raw in rawValues)
        {
            var repository = ParseOne(raw);
            if (repository is null)
                return SavedRepositoryParseResult.Fail(Malformed);
            var error = Validate(repository);
            if (error is not null)
                return SavedRepositoryParseResult.Fail(error);
            parsed.Add(repository);
        }

        return new SavedRepositoryParseResult(parsed.Distinct().ToList(), null);
    }

    private static SavedRepositoryRef? ParseOne(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            var dto = JsonSerializer.Deserialize<SavedRepositoryDto>(raw);
            return dto is { Provider: not null, Owner: not null, Repository: not null, Branch: not null }
                ? new SavedRepositoryRef(dto.Provider, dto.Owner, dto.Repository, dto.Branch)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly (Func<SavedRepositoryRef, bool> IsValid, string Error)[] Rules =
    [
        (r => r.Provider is GitHub or AzureDevOps, "Saved repository provider must be 'github' or 'azure-devops'."),
        (r => IsName(r.Owner, MaxOwnerLength) && !r.Owner.Contains('/'), "Saved repository owner is not valid."),
        (r => IsName(r.Repository, MaxRepositoryLength) && HasExpectedSegments(r), "Saved repository name is not valid."),
        (r => IsName(r.Branch, MaxBranchLength), "Saved repository branch is not valid."),
    ];

    private static string? Validate(SavedRepositoryRef repository) =>
        Rules.Where(rule => !rule.IsValid(repository)).Select(rule => rule.Error).FirstOrDefault();

    private static bool HasExpectedSegments(SavedRepositoryRef repository)
    {
        var expected = repository.Provider == AzureDevOps ? 2 : 1;
        var segments = repository.Repository.Split('/');
        return segments.Length == expected && segments.All(s => s.Length > 0);
    }

    private static bool IsName(string value, int maxLength) =>
        value.Length > 0
        && value.Length <= maxLength
        && value.Trim() == value
        && !value.Any(char.IsControl)
        && !value.Split('/').Any(segment => segment is "." or "..");

    private sealed class SavedRepositoryDto
    {
        [JsonPropertyName("provider")] public string? Provider { get; init; }
        [JsonPropertyName("owner")] public string? Owner { get; init; }
        [JsonPropertyName("repository")] public string? Repository { get; init; }
        [JsonPropertyName("branch")] public string? Branch { get; init; }
    }
}
