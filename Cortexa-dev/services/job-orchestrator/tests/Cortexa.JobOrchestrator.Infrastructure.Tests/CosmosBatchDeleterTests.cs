using System.Net;
using Cortexa.JobOrchestrator.Application.Constants;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests;

public sealed class CosmosBatchDeleterTests
{
    private const string DatabaseName = "cortexa-pipeline";
    private const string BatchId = "batch-abc123";

    private static CosmosSettings BuildSettings() => new()
    {
        Database = DatabaseName,
        DocumentsContainer = "documents",
        ChunksContainer = "chunks",
        ProvenanceMapsContainer = "provenance_maps",
        CandidatesContainer = "candidates",
        EvidenceBundlesContainer = "evidence_bundles",
        VerdictsContainer = "verdicts",
        HarvestingContainer = "reports",
        SeedingContainer = "reports",
        BatchesContainer = "batches",
        ConfigContainer = "config"
    };

    private static DeleteBatchContext BuildContext() => new(
        BatchId,
        new DeletionRequestOptions("operator-1", "corr-1", Force: false),
        new BatchAccess("org-1", IsSuperAdmin: false));

    [Fact]
    public void StoreName_ReturnsCosmosLiteral()
    {
        var client = Substitute.For<CosmosClient>();
        var retryPolicy = Substitute.For<IRetryPolicy>();
        var deleter = new CosmosBatchDeleter(client, Options.Create(BuildSettings()), retryPolicy);

        deleter.StoreName.Should().Be("Cosmos");
    }

    [Fact]
    public void DeleterContainerSet_MatchesAuthoritativeDataContainersExcludingSaga()
    {
        BatchScopedContainers.DataContainersExcludingSaga.Should().BeEquivalentTo(new[]
        {
            "documents",
            "chunks",
            "provenance_maps",
            "candidates",
            "evidence_bundles",
            "verdicts",
            "reports",
            "harvesting",
            "seeding"
        });

        BatchScopedContainers.DataContainersExcludingSaga.Should().NotContain(BatchScopedContainers.SagaContainer);
    }

    public sealed record IdRecord(string Id);
}
