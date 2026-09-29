using System.Reflection;
using System.Text.Json;
using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Api.Endpoints;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class SagaStatusEndpointsTests
{
    private static readonly MethodInfo HandleGetStatusMethod = typeof(SagaStatusEndpoints)
        .GetMethod("HandleGetStatus", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static async Task<IResult> InvokeHandleGetStatus(
        string id,
        ISagaRepository sagaRepo,
        IDocumentRepository documentRepo,
        HttpContext ctx)
    {
        var settings = Options.Create(new OrchestratorSettings());
        var invocationResult = HandleGetStatusMethod.Invoke(
            null, [id, sagaRepo, documentRepo, settings, ctx, CancellationToken.None]);
        return await (Task<IResult>)invocationResult!;
    }

    private static BatchSaga BuildSaga(string id, bool wantsHarvesting, bool wantsSeeding)
    {
        var documents = new List<DocumentProgress> { new("doc-1", DocumentState.Complete) };

        return new BatchSaga(
            id,
            BatchState.Completed,
            documents,
            wantsHarvesting: wantsHarvesting,
            wantsSeeding: wantsSeeding,
            version: 1,
            eTag: null,
            schemaVersion: 1);
    }

    private static (ISagaRepository Sagas, IDocumentRepository Documents) BuildRepos(BatchSaga saga)
    {
        var sagas = Substitute.For<ISagaRepository>();
        var documents = Substitute.For<IDocumentRepository>();
        sagas.GetAsync(saga.Id, Arg.Any<CancellationToken>()).Returns(saga);
        documents.ListByBatchAsync(saga.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DocumentRecord>)[]);
        return (sagas, documents);
    }

    [Fact]
    public async Task HandleGetStatus_HarvestingOnlySaga_ReturnsWantsHarvestingTrueAndWantsSeedingFalse()
    {
        const string batchId = "batch-harvesting";
        var saga = BuildSaga(batchId, wantsHarvesting: true, wantsSeeding: false);
        var (sagas, documents) = BuildRepos(saga);
        var ctx = new DefaultHttpContext();

        var result = await InvokeHandleGetStatus(batchId, sagas, documents, ctx);

        var ok = result.Should().BeOfType<Ok<ApiResponse<BatchStatusResponse>>>().Subject;
        ok.Value!.Data!.WantsHarvesting.Should().BeTrue();
        ok.Value.Data.WantsSeeding.Should().BeFalse();
    }

    [Fact]
    public async Task HandleGetStatus_SeedingOnlySaga_ReturnsWantsSeedingTrueAndWantsHarvestingFalse()
    {
        const string batchId = "batch-seeding";
        var saga = BuildSaga(batchId, wantsHarvesting: false, wantsSeeding: true);
        var (sagas, documents) = BuildRepos(saga);
        var ctx = new DefaultHttpContext();

        var result = await InvokeHandleGetStatus(batchId, sagas, documents, ctx);

        var ok = result.Should().BeOfType<Ok<ApiResponse<BatchStatusResponse>>>().Subject;
        ok.Value!.Data!.WantsHarvesting.Should().BeFalse();
        ok.Value.Data.WantsSeeding.Should().BeTrue();
    }

    [Fact]
    public async Task HandleGetStatus_DualEngineSaga_ReturnsBothFlagsTrue()
    {
        const string batchId = "batch-dual";
        var saga = BuildSaga(batchId, wantsHarvesting: true, wantsSeeding: true);
        var (sagas, documents) = BuildRepos(saga);
        var ctx = new DefaultHttpContext();

        var result = await InvokeHandleGetStatus(batchId, sagas, documents, ctx);

        var ok = result.Should().BeOfType<Ok<ApiResponse<BatchStatusResponse>>>().Subject;
        ok.Value!.Data!.WantsHarvesting.Should().BeTrue();
        ok.Value.Data.WantsSeeding.Should().BeTrue();
    }

    [Fact]
    public async Task HandleGetStatus_BatchNotFound_ReturnsBatchNotFoundNotFound()
    {
        const string batchId = "batch-missing";
        var sagas = Substitute.For<ISagaRepository>();
        var documents = Substitute.For<IDocumentRepository>();
        sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);
        var ctx = new DefaultHttpContext();

        var result = await InvokeHandleGetStatus(batchId, sagas, documents, ctx);

        var notFound = result.Should().BeOfType<NotFound<ApiResponse<object>>>().Subject;
        notFound.Value!.ErrorCode.Should().Be("BATCH_NOT_FOUND");
    }

    [Fact]
    public void BatchStatusResponse_WithCreatedAt_SerializesToSnakeCaseCreatedAt()
    {
        var response = new BatchStatusResponse
        {
            BatchId = "batch-123",
            BatchName = "Test Batch",
            Status = "Completed",
            WantsHarvesting = true,
            WantsSeeding = false,
            CreatedAt = new DateTimeOffset(2026, 7, 8, 10, 30, 0, TimeSpan.Zero).ToString("O"),
            Documents = [],
            UnavailableSources = []
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(response, options);

        json.Should().Contain("\"created_at\":");
        json.Should().Contain("\"2026-07-08T10:30:00");
    }
}
