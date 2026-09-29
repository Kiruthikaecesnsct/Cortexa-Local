using System.Reflection;
using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Api.Endpoints;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class BatchLifecycleEndpointsStartBatchTests
{
    private static readonly MethodInfo HandleStartBatchMethod = typeof(BatchLifecycleEndpoints)
        .GetMethod("HandleStartBatch", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static StartBatchHandler BuildHandler(
        out ISagaRepository sagas,
        out IDocumentRepository documents,
        out IEventPublisher publisher)
    {
        sagas = Substitute.For<ISagaRepository>();
        documents = Substitute.For<IDocumentRepository>();
        publisher = Substitute.For<IEventPublisher>();
        return new StartBatchHandler(sagas, documents, publisher);
    }

    private static async Task<IResult> InvokeHandleStartBatch(
        StartBatchHandler handler,
        HttpContext ctx,
        StartBatchRequest request)
    {
        var invocationResult = HandleStartBatchMethod.Invoke(null, [handler, ctx, request, CancellationToken.None]);
        return await (Task<IResult>)invocationResult!;
    }

    [Fact]
    public async Task HandleStartBatch_NullBatchId_ReturnsValidationErrorBadRequest()
    {
        var handler = BuildHandler(out _, out _, out _);
        var ctx = new DefaultHttpContext();
        var request = new StartBatchRequest(null!);

        var result = await InvokeHandleStartBatch(handler, ctx, request);

        var badRequest = result.Should().BeOfType<BadRequest<ApiResponse<object>>>().Subject;
        badRequest.Value!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleStartBatch_WhitespaceBatchId_ReturnsValidationErrorBadRequest(string batchId)
    {
        var handler = BuildHandler(out _, out _, out _);
        var ctx = new DefaultHttpContext();
        var request = new StartBatchRequest(batchId);

        var result = await InvokeHandleStartBatch(handler, ctx, request);

        var badRequest = result.Should().BeOfType<BadRequest<ApiResponse<object>>>().Subject;
        badRequest.Value!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task HandleStartBatch_ValidBatchIdNotFound_ReturnsBatchNotFoundNotFound()
    {
        const string batchId = "batch-missing";
        var handler = BuildHandler(out var sagas, out _, out _);
        sagas.GetAsync(batchId, Arg.Any<CancellationToken>()).Returns((Domain.Entities.BatchSaga?)null);
        var ctx = new DefaultHttpContext();
        var request = new StartBatchRequest(batchId);

        var result = await InvokeHandleStartBatch(handler, ctx, request);

        var notFound = result.Should().BeOfType<NotFound<ApiResponse<object>>>().Subject;
        notFound.Value!.ErrorCode.Should().Be("BATCH_NOT_FOUND");
    }
}
