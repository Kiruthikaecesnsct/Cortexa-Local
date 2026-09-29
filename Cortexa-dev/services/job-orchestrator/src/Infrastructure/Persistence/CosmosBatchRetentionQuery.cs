using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class CosmosBatchRetentionQuery : IBatchRetentionQuery
{
    private static readonly QuerySpec FailedSpec = new("Failed", "Failed batch retention age exceeded");
    private static readonly QuerySpec QueuedSpec = new("Queued", "Queued batch stuck beyond threshold");
    private static readonly QuerySpec InProgressSpec = new("InProgress", "InProgress batch wedged beyond threshold");

    private readonly Container _container;
    private readonly int _pageSize;

    public CosmosBatchRetentionQuery(
        CosmosClient client,
        IOptions<CosmosSettings> cosmosSettings,
        IOptions<RetentionPolicyOptions> retentionOptions)
    {
        var cosmos = cosmosSettings.Value;
        _container = client.GetContainer(cosmos.Database, cosmos.BatchesContainer);
        _pageSize = retentionOptions.Value.BatchPageSize;
    }

    public Task<IReadOnlyList<RetentionCandidate>> FindFailedOlderThanAsync(
        DateTimeOffset cutoffUtc, int page, CancellationToken ct) =>
        QueryByStateAsync(FailedSpec, cutoffUtc, page, ct);

    public Task<IReadOnlyList<RetentionCandidate>> FindStuckIncompleteAsync(
        DateTimeOffset cutoffUtc, int page, CancellationToken ct) =>
        QueryByStateAsync(QueuedSpec, cutoffUtc, page, ct);

    public Task<IReadOnlyList<RetentionCandidate>> FindStuckInProgressAsync(
        DateTimeOffset cutoffUtc, int page, CancellationToken ct) =>
        QueryByStateAsync(InProgressSpec, cutoffUtc, page, ct);

    private Task<IReadOnlyList<RetentionCandidate>> QueryByStateAsync(
        QuerySpec spec,
        DateTimeOffset cutoffUtc,
        int page,
        CancellationToken ct)
    {
        var cutoffEpoch = cutoffUtc.ToUnixTimeSeconds();
        var sql = "SELECT c.id, c.state, c.created_at, c.failed_at, c._ts "
                + "FROM c WHERE c.state = @state AND c._ts <= @cutoff "
                + $"OFFSET {page * _pageSize} LIMIT {_pageSize}";

        var query = new QueryDefinition(sql)
            .WithParameter("@state", spec.State)
            .WithParameter("@cutoff", cutoffEpoch);

        return ExecuteProjectionQueryAsync(query, spec.Reason, ct);
    }

    private sealed record QuerySpec(string State, string Reason);

    private async Task<IReadOnlyList<RetentionCandidate>> ExecuteProjectionQueryAsync(
        QueryDefinition query,
        string reason,
        CancellationToken ct)
    {
        var iterator = _container.GetItemQueryIterator<RetentionProjection>(query);
        var results = new List<RetentionCandidate>();

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            results.AddRange(page.Select(p => MapToCandidate(p, reason)));
        }

        return results;
    }

    private static RetentionCandidate MapToCandidate(RetentionProjection projection, string reason)
    {
        var lastActivity = DateTimeOffset.FromUnixTimeSeconds(projection.Ts);
        return new RetentionCandidate(
            projection.Id,
            projection.State,
            projection.CreatedAt,
            projection.FailedAt,
            lastActivity,
            reason);
    }

    private sealed class RetentionProjection
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("failed_at")]
        public DateTimeOffset? FailedAt { get; set; }

        [JsonPropertyName("_ts")]
        public long Ts { get; set; }
    }
}
