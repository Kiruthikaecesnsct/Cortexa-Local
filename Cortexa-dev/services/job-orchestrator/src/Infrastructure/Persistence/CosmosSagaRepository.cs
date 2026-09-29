using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Helpers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class CosmosSagaRepository : ISagaRepository
{
    private readonly Container _container;
    private readonly IRetryPolicy _retryPolicy;
    private readonly CosmosSettings _settings;

    public CosmosSagaRepository(CosmosClient client, IOptions<CosmosSettings> settings, IRetryPolicy retryPolicy)
    {
        var s = settings.Value;
        _container = client.GetContainer(s.Database, s.BatchesContainer);
        _retryPolicy = retryPolicy;
        _settings = s;
    }

    public async Task<BatchSaga?> GetAsync(string batchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            return null;

        try
        {
            var response = await _container.ReadItemAsync<SagaDocument>(
                batchId,
                new PartitionKey(batchId),
                cancellationToken: ct);

            return MapToDomain(response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CreateAsync(BatchSaga saga, CancellationToken ct)
    {
        var document = MapToDocument(saga);
        await _container.CreateItemAsync(document, new PartitionKey(saga.Id), cancellationToken: ct);
    }

    public async Task UpdateAsync(BatchSaga saga, CancellationToken ct)
    {
        var document = MapToDocument(saga);
        var options = new ItemRequestOptions { IfMatchEtag = saga.ETag };

        try
        {
            var response = await _container.ReplaceItemAsync(
                document,
                saga.Id,
                new PartitionKey(saga.Id),
                options,
                ct);

            saga.ETag = response.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            throw new ConcurrencyConflictException(saga.Id);
        }
    }

    public async Task DeleteAsync(string batchId, CancellationToken ct)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.SagaDeleteDeadlineSeconds));

        await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                try
                {
                    await _container.DeleteItemAsync<SagaDocument>(
                        batchId,
                        new PartitionKey(batchId),
                        cancellationToken: cts.Token);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                }
            },
            _retryPolicy,
            ct: cts.Token,
            externalCt: ct);
    }

    public async Task<IReadOnlyList<BatchSaga>> ListAsync(CancellationToken ct)
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c._ts DESC OFFSET 0 LIMIT 200");
        var iterator = _container.GetItemQueryIterator<SagaDocument>(query);
        var result = new List<BatchSaga>();

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            result.AddRange(page.Select(doc => MapToDomain(doc, null)));
        }

        return result;
    }

    public async Task<IReadOnlyCollection<string>> ListStalledBatchIdsAsync(DateTimeOffset cutoffUtc, CancellationToken ct)
    {
        var query = new QueryDefinition(
                "SELECT c.id FROM c WHERE c.state = @state AND c._ts < @cutoffEpochSeconds")
            .WithParameter("@state", BatchState.InProgress.ToString())
            .WithParameter("@cutoffEpochSeconds", cutoffUtc.ToUnixTimeSeconds());

        var iterator = _container.GetItemQueryIterator<StalledBatchIdProjection>(query);
        var result = new List<string>();

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            result.AddRange(page.Select(p => p.Id));
        }

        return result;
    }

    private static BatchSaga MapToDomain(SagaDocument doc, string? eTag)
    {
        var documents = doc.Documents
            .Select(d => DocumentProgress.FromPersistence(
                new DocumentProgressPersistenceData(
                    d.DocumentId,
                    ParseDocState(d.State),
                    d.FailureReason,
                    d.ExpectedCandidateCount,
                    d.CompletedCandidateIds,
                    d.FailedCandidateIds,
                    d.ExpectedExtractionUnits,
                    d.ReceivedExtractionUnitIndices,
                    d.SeededCandidateIds ?? [],
                    d.CompletedSeededCandidateIds ?? [],
                    d.FailedSeededCandidateIds ?? [])))
            .ToList();

        var metadata = doc.BatchName is not null || doc.CreatedAt.HasValue
            ? new BatchMetadata(
                BatchName: doc.BatchName,
                Engine: doc.Engine,
                AiModel: doc.AiModel,
                SeedCorpusDomain: doc.SeedCorpusDomain,
                GitRepoUrl: doc.GitRepoUrl,
                GitPatSecretName: doc.GitPatSecretName,
                GitBranch: doc.GitBranch,
                GitHost: doc.GitHost,
                CreatedAt: doc.CreatedAt,
                TotalDocumentCount: doc.TotalDocumentCount,
                OwnerOrgId: doc.OrgId,
                OwnerUserId: doc.OwnerUserId,
                ExtractionModel: doc.ExtractionModel,
                PrimaryEvidenceModel: doc.PrimaryEvidenceModel,
                ScoringModel: doc.ScoringModel,
                SeedingModel: doc.SeedingModel,
                SeedingMode: SeedingModes.Normalize(doc.SeedingMode))
            : null;

        return new BatchSaga(
            doc.Id,
            ParseBatchState(doc.State),
            documents,
            doc.WantsHarvesting,
            doc.WantsSeeding,
            doc.Version,
            eTag,
            doc.SchemaVersion,
            doc.FailureReason,
            doc.FailedAt,
            activeDocumentIds: new HashSet<string>(doc.ActiveDocumentIds),
            queuedDocumentIds: new Queue<string>(doc.QueuedDocumentIds),
            metadata: metadata,
            cancelledAt: doc.CancelledAt,
            evidenceCompletedCount: doc.EvidenceCompletedCount,
            evidenceSourceLiveCounts: doc.EvidenceSourceLiveCounts ?? [],
            countedEvidenceCandidateIds: new HashSet<string>(doc.CountedEvidenceCandidateIds ?? []),
            expectedAssetEmbeddingUnits: doc.ExpectedAssetEmbeddingUnits,
            completedAssetEmbeddingUnits: doc.CompletedAssetEmbeddingUnits,
            recordedAssetEmbeddingUnitKeys: new HashSet<string>(doc.RecordedAssetEmbeddingUnitKeys ?? []),
            recordedDigestRequestedDocumentIds: new HashSet<string>(doc.RecordedDigestRequestedDocumentIds ?? []),
            recordedDigestCompletedDocumentIds: new HashSet<string>(doc.RecordedDigestCompletedDocumentIds ?? []),
            recordedLandscapeRequestedDocumentIds: new HashSet<string>(doc.RecordedLandscapeRequestedDocumentIds ?? []),
            recordedLandscapeCompletedDocumentIds: new HashSet<string>(doc.RecordedLandscapeCompletedDocumentIds ?? []));
    }

    private static SagaDocument MapToDocument(BatchSaga saga)
    {
        return new SagaDocument
        {
            Id = saga.Id,
            BatchId = saga.Id,
            State = saga.State.ToString(),
            Documents = saga.Documents
                .Select(d => new DocumentProgressRecord
                {
                    DocumentId = d.DocumentId,
                    State = d.State.ToString(),
                    FailureReason = d.FailureReason,
                    ExpectedCandidateCount = d.ExpectedCandidateCount,
                    CompletedCandidateIds = d.CompletedCandidateIds.ToList(),
                    FailedCandidateIds = d.FailedCandidateIds.ToList(),
                    ExpectedExtractionUnits = d.ExpectedExtractionUnits,
                    ReceivedExtractionUnitIndices = d.ReceivedExtractionUnitIndices.ToList(),
                    SeededCandidateIds = d.SeededCandidateIds.ToList(),
                    CompletedSeededCandidateIds = d.CompletedSeededCandidateIds.ToList(),
                    FailedSeededCandidateIds = d.FailedSeededCandidateIds.ToList()
                })
                .ToList(),
            WantsHarvesting = saga.WantsHarvesting,
            WantsSeeding = saga.WantsSeeding,
            Version = saga.Version,
            SchemaVersion = saga.SchemaVersion,
            FailureReason = saga.FailureReason,
            FailedAt = saga.FailedAt,
            CancelledAt = saga.CancelledAt,
            ActiveDocumentIds = saga.ActiveDocumentIds.ToList(),
            QueuedDocumentIds = saga.QueuedDocumentIds.ToList(),
            ActiveCount = saga.ActiveCount,
            CompletedCount = saga.CompletedCount,
            BatchName = saga.Metadata?.BatchName,
            Engine = saga.Metadata?.Engine,
            AiModel = saga.Metadata?.AiModel,
            SeedCorpusDomain = saga.Metadata?.SeedCorpusDomain,
            GitRepoUrl = saga.Metadata?.GitRepoUrl,
            GitPatSecretName = saga.Metadata?.GitPatSecretName,
            GitBranch = saga.Metadata?.GitBranch,
            GitHost = saga.Metadata?.GitHost,
            CreatedAt = saga.Metadata?.CreatedAt,
            TotalDocumentCount = saga.Metadata?.TotalDocumentCount ?? 0,
            OrgId = saga.Metadata?.OwnerOrgId,
            OwnerUserId = saga.Metadata?.OwnerUserId,
            ExtractionModel = saga.Metadata?.ExtractionModel,
            PrimaryEvidenceModel = saga.Metadata?.PrimaryEvidenceModel,
            ScoringModel = saga.Metadata?.ScoringModel,
            SeedingModel = saga.Metadata?.SeedingModel,
            SeedingMode = saga.Metadata?.SeedingMode,
            EvidenceCompletedCount = saga.EvidenceCompletedCount,
            EvidenceSourceLiveCounts = saga.EvidenceSourceLiveCounts,
            CountedEvidenceCandidateIds = saga.CountedEvidenceCandidateIds.ToList(),
            ExpectedAssetEmbeddingUnits = saga.ExpectedAssetEmbeddingUnits,
            CompletedAssetEmbeddingUnits = saga.CompletedAssetEmbeddingUnits,
            RecordedAssetEmbeddingUnitKeys = saga.RecordedAssetEmbeddingUnitKeys.ToList(),
            RecordedDigestRequestedDocumentIds = saga.RecordedDigestRequestedDocumentIds.ToList(),
            RecordedDigestCompletedDocumentIds = saga.RecordedDigestCompletedDocumentIds.ToList(),
            RecordedLandscapeRequestedDocumentIds = saga.RecordedLandscapeRequestedDocumentIds.ToList(),
            RecordedLandscapeCompletedDocumentIds = saga.RecordedLandscapeCompletedDocumentIds.ToList()
        };
    }

    private static BatchState ParseBatchState(string value) =>
        Enum.TryParse<BatchState>(value, ignoreCase: true, out var state) ? state : BatchState.Queued;

    private static DocumentState ParseDocState(string value) =>
        Enum.TryParse<DocumentState>(value, ignoreCase: true, out var state) ? state : DocumentState.Queued;

    private sealed class StalledBatchIdProjection
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
