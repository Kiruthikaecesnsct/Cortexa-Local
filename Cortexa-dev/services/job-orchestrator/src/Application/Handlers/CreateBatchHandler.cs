using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class CreateBatchHandler
{
    private readonly IBlobStorageWriter _blob;
    private readonly IDocumentRepository _documents;
    private readonly ISagaRepository _sagas;
    private readonly IGitPatSecretStore _gitPatStore;
    private readonly Application.Contracts.IConfigRepository _configRepository;

    public CreateBatchHandler(
        IBlobStorageWriter blob,
        IDocumentRepository documents,
        ISagaRepository sagas,
        IGitPatSecretStore gitPatStore,
        Application.Contracts.IConfigRepository configRepository)
    {
        _blob = blob;
        _documents = documents;
        _sagas = sagas;
        _gitPatStore = gitPatStore;
        _configRepository = configRepository;
    }

    public async Task<CreateBatchResponse> HandleAsync(CreateBatchCommand command, CancellationToken ct)
    {
        var batchId = Guid.NewGuid().ToString();
        var records = await UploadFilesAsync(batchId, command.Files, ct);

        if (records.Count == 0 && !string.IsNullOrWhiteSpace(command.RepoUrl))
        {
            var repoDoc = CreateRepoDocument(batchId, command.RepoUrl);
            records.Add(repoDoc);
        }

        await _documents.CreateManyAsync(records, ct);

        var gitPatSecretName = await StoreGitPatIfNeededAsync(
            batchId,
            command.GitPatRaw,
            ct);

        var config = await _configRepository.GetAsync(ct);

        var metadata = new BatchMetadata(
            BatchName: command.BatchName,
            Engine: command.Engine,
            AiModel: command.AiModel,
            SeedCorpusDomain: command.SeedCorpusDomain,
            GitRepoUrl: command.RepoUrl,
            GitPatSecretName: gitPatSecretName,
            GitBranch: command.GitBranch,
            GitHost: command.GitProvider,
            CreatedAt: DateTimeOffset.UtcNow,
            TotalDocumentCount: records.Count,
            OwnerOrgId: command.OrgId,
            OwnerUserId: command.UserId,
            ExtractionModel: config.ExtractionModel,
            PrimaryEvidenceModel: config.PrimaryEvidenceModel,
            ScoringModel: config.ScoringModel,
            SeedingModel: config.SeedingModel,
            SeedingMode: config.SeedingMode);

        var saga = new BatchSaga(
            batchId,
            BatchState.Queued,
            [],
            wantsHarvesting: string.Equals(command.Engine, "harvesting", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(command.Engine, "dual", StringComparison.OrdinalIgnoreCase),
            wantsSeeding: string.Equals(command.Engine, "seeding", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(command.Engine, "dual", StringComparison.OrdinalIgnoreCase),
            version: 0,
            eTag: null,
            schemaVersion: 1,
            metadata: metadata);

        await _sagas.CreateAsync(saga, ct);

        return new CreateBatchResponse(batchId, records.Count, "pending");
    }

    private async Task<string?> StoreGitPatIfNeededAsync(
        string batchId,
        string? gitPatRaw,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gitPatRaw))
            return null;

        return await _gitPatStore.StoreAsync(batchId, gitPatRaw, ct);
    }

    private async Task<List<DocumentRecord>> UploadFilesAsync(
        string batchId,
        IReadOnlyList<(string Filename, Stream Content, string ContentType)> files,
        CancellationToken ct)
    {
        var records = new List<DocumentRecord>(files.Count);
        foreach (var (filename, content, contentType) in files)
        {
            var documentId = Guid.NewGuid().ToString();
            var blobUri = await _blob.SaveRawAsync(batchId, documentId, content, contentType, ct);
            records.Add(new DocumentRecord(documentId, batchId, filename, blobUri));
        }
        return records;
    }

    private static DocumentRecord CreateRepoDocument(string batchId, string repoUrl)
    {
        var documentId = Guid.NewGuid().ToString();
        var filename = DeriveFilenameFromRepoUrl(repoUrl);
        return new DocumentRecord(documentId, batchId, filename, string.Empty)
        {
            SourceKind = "code"
        };
    }

    private static string DeriveFilenameFromRepoUrl(string repoUrl)
    {
        var uri = new Uri(repoUrl);
        var path = uri.AbsolutePath.Trim('/');
        return string.IsNullOrWhiteSpace(path) ? "repository" : path.Replace('/', '-');
    }
}
