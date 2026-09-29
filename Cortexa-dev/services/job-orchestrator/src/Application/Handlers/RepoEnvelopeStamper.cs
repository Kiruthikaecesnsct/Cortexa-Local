using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public static class RepoEnvelopeStamper
{
    public static void StampRepoFields(EventEnvelope envelope, BatchSaga saga)
    {
        if (!ShouldStampRepo(envelope, saga))
            return;

        var metadata = saga.Metadata!;
        envelope.Payload["source_kind"] = "code";
        envelope.Payload["repo_url"] = metadata.GitRepoUrl!;

        if (!string.IsNullOrWhiteSpace(metadata.GitBranch))
            envelope.Payload["git_branch"] = metadata.GitBranch;

        if (!string.IsNullOrWhiteSpace(metadata.GitHost))
            envelope.Payload["git_host"] = metadata.GitHost;

        if (!string.IsNullOrWhiteSpace(metadata.GitPatSecretName))
            envelope.Payload["git_pat_secret_name"] = metadata.GitPatSecretName;
    }

    private static bool ShouldStampRepo(EventEnvelope envelope, BatchSaga saga)
    {
        return envelope.EventType.Equals(SagaEventType.IngestionRequested, StringComparison.OrdinalIgnoreCase)
               && saga.Metadata is not null
               && !string.IsNullOrWhiteSpace(saga.Metadata.GitRepoUrl);
    }
}
