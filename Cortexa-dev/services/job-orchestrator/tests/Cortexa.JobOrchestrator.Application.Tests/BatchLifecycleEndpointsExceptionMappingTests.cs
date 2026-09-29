using Cortexa.JobOrchestrator.Api.Endpoints;
using Cortexa.JobOrchestrator.Application.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class BatchLifecycleEndpointsExceptionMappingTests
{
    private const string CorrelationId = "corr-1";

    [Fact]
    public void MapDeleteException_InvalidBatchStateException_ReturnsBadRequest()
    {
        var result = BatchLifecycleEndpoints.MapDeleteException(new InvalidBatchStateException("batch-1"), CorrelationId);

        result.Should().BeOfType<BadRequest<Cortexa.JobOrchestrator.Api.Contracts.ApiResponse<object>>>();
    }

    [Fact]
    public void MapDeleteException_BatchNotFoundException_ReturnsNotFound()
    {
        var result = BatchLifecycleEndpoints.MapDeleteException(new BatchNotFoundException("batch-1"), CorrelationId);

        result.Should().BeOfType<NotFound<Cortexa.JobOrchestrator.Api.Contracts.ApiResponse<object>>>();
    }

    [Fact]
    public void MapDeleteException_CrossOrgAccessException_ReturnsForbiddenStatusCode()
    {
        var result = BatchLifecycleEndpoints.MapDeleteException(new CrossOrgAccessException("batch-1"), CorrelationId);

        var jsonResult = result.Should().BeOfType<JsonHttpResult<Cortexa.JobOrchestrator.Api.Contracts.ApiResponse<object>>>().Subject;
        jsonResult.StatusCode.Should().Be(403);
    }
}
