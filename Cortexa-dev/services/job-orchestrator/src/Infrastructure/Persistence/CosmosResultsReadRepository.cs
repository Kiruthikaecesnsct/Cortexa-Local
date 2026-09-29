using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class CosmosResultsReadRepository : IResultsReadRepository
{
    private readonly CosmosClient _client;
    private readonly CosmosSettings _settings;

    private static readonly IReadOnlyDictionary<string, string> BackendToFrontendKeyMap = new Dictionary<string, string>
    {
        ["PatentApi"] = "patent_api",
        ["SeedCorpus"] = "vector_corpus",
        ["LlmResearch"] = "llm_deep_research"
    };

    public CosmosResultsReadRepository(CosmosClient client, IOptions<CosmosSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<HarvestingResultRecord?> GetHarvestingResultAsync(string batchId, CancellationToken ct)
    {
        var container = _client.GetContainer(_settings.Database, _settings.HarvestingContainer);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.batch_id = @batchId AND (NOT IS_DEFINED(c.engine) OR c.engine != @seedingEngine)")
            .WithParameter("@batchId", batchId)
            .WithParameter("@seedingEngine", "seeding");

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(batchId) };
        var iterator = container.GetItemQueryIterator<ReportEnvelopeDto>(query, requestOptions: options);

        var items = new List<ReportEnvelopeDto>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            items.AddRange(page);
        }

        if (items.Count == 0)
            return null;

        return await ReassembleHarvestingReport(batchId, items, ct);
    }

    private async Task<HarvestingResultRecord?> ReassembleHarvestingReport(string batchId, List<ReportEnvelopeDto> items, CancellationToken ct)
    {
        if (IsLegacySingleDoc(items))
            return await DeserializeLegacyReport(batchId, ct);

        var headers = await FetchItemsByDocType<ReportHeaderDto>(batchId, "report_header", ct);

        if (headers.Count == 0)
            return null;

        var candidateDtos = await FetchItemsByDocType<ReportCandidateDto>(batchId, "report_candidate", ct);

        var header = headers[0];
        var sortedCandidates = candidateDtos.OrderBy(c => c.Rank).ToList();
        var candidates = sortedCandidates.Select(MapToCandidate).ToList();

        return new HarvestingResultRecord
        {
            Id = header.Id,
            BatchId = header.BatchId,
            DocumentId = header.DocumentId,
            Engine = header.Engine,
            GeneratedAt = header.GeneratedAt,
            Candidates = candidates
        };
    }

    private async Task<List<T>> FetchItemsByDocType<T>(string batchId, string docType, CancellationToken ct)
    {
        var container = _client.GetContainer(_settings.Database, _settings.HarvestingContainer);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.batch_id = @batchId AND c.doc_type = @docType")
            .WithParameter("@batchId", batchId)
            .WithParameter("@docType", docType);

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(batchId) };
        var iterator = container.GetItemQueryIterator<T>(query, requestOptions: options);

        var results = new List<T>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            results.AddRange(page);
        }

        return results;
    }

    private static bool IsLegacySingleDoc(List<ReportEnvelopeDto> items)
    {
        return items.Count == 1 && items[0].Candidates is not null && items[0].DocType is null;
    }

    private async Task<HarvestingResultRecord?> DeserializeLegacyReport(string batchId, CancellationToken ct)
    {
        return await ReadSingleByBatchIdAsync<HarvestingResultRecord>(
            _settings.HarvestingContainer, batchId, "harvesting", ct);
    }

    private static CandidateResultDto MapToCandidate(ReportCandidateDto dto)
    {
        return new CandidateResultDto
        {
            Id = dto.CandidateId,
            CandidateId = dto.CandidateId,
            Title = dto.Title,
            Description = dto.Description,
            ClaimDraft = dto.ClaimDraft,
            Maturity = dto.Maturity,
            Rank = dto.Rank,
            WeightedScore = dto.WeightedScore,
            Axes = dto.Axes,
            AgreementFlag = dto.AgreementFlag,
            Citations = dto.Citations,
            ProvenanceLinks = dto.ProvenanceLinks,
            SourceAvailability = dto.SourceAvailability,
            SourceStatus = dto.SourceStatus
        };
    }

    public async Task<SeedingReportRecord?> GetSeedingResultAsync(string batchId, CancellationToken ct)
    {
        return await ReadSingleByBatchIdAsync<SeedingReportRecord>(
            _settings.SeedingContainer, batchId, "seeding", ct);
    }

    public async Task<CandidateResultDto?> GetCandidateDetailAsync(string batchId, string candidateId, CancellationToken ct)
    {
        var candidate = await ReadCandidateFromReport(batchId, candidateId, ct);
        if (candidate is null)
            return null;

        var verdict = await ReadItemAsync<VerdictResultDto>(_settings.VerdictsContainer, candidateId, batchId, ct);
        var candidateDoc = await ReadItemAsync<CandidateDocument>(_settings.CandidatesContainer, candidateId, batchId, ct);

        if (!string.IsNullOrEmpty(verdict?.DraftedClaim))
            candidate.ClaimDraft = verdict.DraftedClaim;
        else if (!string.IsNullOrEmpty(candidateDoc?.ClaimDraft))
            candidate.ClaimDraft = candidateDoc.ClaimDraft;

        return candidate;
    }

    private async Task<CandidateResultDto?> ReadCandidateFromReport(string batchId, string candidateId, CancellationToken ct)
    {
        var container = _client.GetContainer(_settings.Database, _settings.HarvestingContainer);

        var deterministicId = $"report_candidate:{batchId}:{candidateId}";
        var candidateDto = await ReadItemAsync<ReportCandidateDto>(_settings.HarvestingContainer, deterministicId, batchId, ct);

        if (candidateDto is not null)
            return MapToCandidate(candidateDto);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.batch_id = @batchId AND c.doc_type = @docType AND c.candidate_id = @candidateId")
            .WithParameter("@batchId", batchId)
            .WithParameter("@docType", "report_candidate")
            .WithParameter("@candidateId", candidateId);

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(batchId) };
        var iterator = container.GetItemQueryIterator<ReportCandidateDto>(query, requestOptions: options);

        if (!iterator.HasMoreResults)
            return await ReadCandidateFromLegacyReport(batchId, candidateId, ct);

        var page = await iterator.ReadNextAsync(ct);
        var foundCandidate = page.FirstOrDefault();

        if (foundCandidate is not null)
            return MapToCandidate(foundCandidate);

        return await ReadCandidateFromLegacyReport(batchId, candidateId, ct);
    }

    private async Task<CandidateResultDto?> ReadCandidateFromLegacyReport(string batchId, string candidateId, CancellationToken ct)
    {
        var harvestingReport = await DeserializeLegacyReport(batchId, ct);
        return harvestingReport?.Candidates.FirstOrDefault(c => c.Id == candidateId || c.CandidateId == candidateId);
    }

    private async Task<T?> ReadSingleByBatchIdAsync<T>(string containerName, string batchId, string engine, CancellationToken ct)
        where T : class
    {
        var container = _client.GetContainer(_settings.Database, containerName);
        // The 'reports' container holds both engines' reports; every row carries an 'engine' field (harvesting|seeding) written by the Python pipeline services. Filter on it so each getter returns only its engine's report.
        var query = new QueryDefinition("SELECT * FROM c WHERE c.batch_id = @batchId AND c.engine = @engine OFFSET 0 LIMIT 1")
            .WithParameter("@batchId", batchId)
            .WithParameter("@engine", engine);

        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(batchId) };
        var iterator = container.GetItemQueryIterator<T>(query, requestOptions: options);

        if (!iterator.HasMoreResults)
            return null;

        var page = await iterator.ReadNextAsync(ct);
        return page.FirstOrDefault();
    }

    private async Task<T?> ReadItemAsync<T>(string containerName, string id, string partitionKey, CancellationToken ct)
        where T : class
    {
        try
        {
            var container = _client.GetContainer(_settings.Database, containerName);
            var response = await container.ReadItemAsync<T>(id, new PartitionKey(partitionKey), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

}
