using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class GetBatchResultsHandlerTests
{
    private readonly IResultsReadRepository _results;
    private readonly GetBatchResultsHandler _handler;

    public GetBatchResultsHandlerTests()
    {
        _results = Substitute.For<IResultsReadRepository>();
        _handler = new GetBatchResultsHandler(_results);
    }

    private static HarvestingResultRecord BuildHarvestingResult(string batchId) => new()
    {
        Id = "h-1",
        BatchId = batchId,
        DocumentId = "doc-1",
        Engine = "harvesting",
        GeneratedAt = DateTime.UtcNow.ToString("o"),
        Candidates = [new CandidateResultDto { Id = "c-1", CandidateId = "c-1", Title = "Method for X" }]
    };

    private static SeedingReportRecord BuildSeedingResult(string batchId) => new()
    {
        Id = "s-1",
        BatchId = batchId,
        Opportunities = [new SeedingOpportunityDto { Id = "o-1", Title = "New opportunity" }]
    };

    [Fact]
    public async Task HandleAsync_BothResultsAvailable_ReturnsBothInResponse()
    {
        const string batchId = "batch-1";
        _results.GetHarvestingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(BuildHarvestingResult(batchId));
        _results.GetSeedingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(BuildSeedingResult(batchId));

        var result = await _handler.HandleAsync(batchId, CancellationToken.None);

        result.Harvesting.Should().NotBeNull();
        result.Seeding.Should().NotBeNull();
        result.Harvesting!.BatchId.Should().Be(batchId);
        result.Seeding!.BatchId.Should().Be(batchId);
    }

    [Fact]
    public async Task HandleAsync_OnlyHarvestingAvailable_ReturnsSeedingAsNull()
    {
        const string batchId = "batch-1";
        _results.GetHarvestingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(BuildHarvestingResult(batchId));
        _results.GetSeedingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns((SeedingReportRecord?)null);

        var result = await _handler.HandleAsync(batchId, CancellationToken.None);

        result.Harvesting.Should().NotBeNull();
        result.Seeding.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NeitherResultAvailable_ReturnsBothNull()
    {
        const string batchId = "batch-1";
        _results.GetHarvestingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns((HarvestingResultRecord?)null);
        _results.GetSeedingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns((SeedingReportRecord?)null);

        var result = await _handler.HandleAsync(batchId, CancellationToken.None);

        result.Harvesting.Should().BeNull();
        result.Seeding.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_QueriesBothRepositoriesInParallel()
    {
        const string batchId = "batch-1";
        _results.GetHarvestingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(BuildHarvestingResult(batchId));
        _results.GetSeedingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(BuildSeedingResult(batchId));

        await _handler.HandleAsync(batchId, CancellationToken.None);

        await _results.Received(1).GetHarvestingResultAsync(batchId, Arg.Any<CancellationToken>());
        await _results.Received(1).GetSeedingResultAsync(batchId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HarvestingContainsCandidates_MapsCorrectly()
    {
        const string batchId = "batch-1";
        var harvesting = BuildHarvestingResult(batchId);
        _results.GetHarvestingResultAsync(batchId, Arg.Any<CancellationToken>()).Returns(harvesting);
        _results.GetSeedingResultAsync(batchId, Arg.Any<CancellationToken>())
            .Returns((SeedingReportRecord?)null);

        var result = await _handler.HandleAsync(batchId, CancellationToken.None);

        result.Harvesting!.Candidates.Should().HaveCount(1);
        result.Harvesting.Engine.Should().Be("harvesting");
        result.Harvesting.DocumentId.Should().Be("doc-1");
    }
}
