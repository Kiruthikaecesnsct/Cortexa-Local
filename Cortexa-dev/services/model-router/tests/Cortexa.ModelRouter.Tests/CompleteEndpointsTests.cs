using Cortexa.ModelRouter.Api.Endpoints;
using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Application.Interfaces;
using Cortexa.ModelRouter.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cortexa.ModelRouter.Tests;

public sealed class CompleteEndpointsTests
{
    private static readonly TokenUsage SampleUsage = new(10, 20, 30);
    private static readonly CompleteRequest SampleRequest = new("analysis", "What is X?", null, null);

    private static CompleteResponse BuildSampleResponse(string provider = "Foundry") =>
        new(provider, "gpt-5.5", "test response", null, SampleUsage, null);

    private static DualCompleteResponse BuildDualResponse() =>
        new(BuildSampleResponse("Foundry"), BuildSampleResponse("Anthropic"));

    [Fact]
    public async Task CompleteAsync_RouterThrowsTaskCanceledExceptionWithNonCancelledToken_Returns504()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);

        var nonCancelledToken = new CancellationToken(canceled: false);
        router.RouteAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>())
              .Throws(new TaskCanceledException("timeout"));

        var result = await CompleteEndpoints.CompleteAsync(
            SampleRequest, router, loggerFactory, new DefaultHttpContext(), nonCancelledToken);

        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result;
        problemResult.StatusCode.Should().Be(StatusCodes.Status504GatewayTimeout);
    }

    [Fact]
    public async Task CompleteAsync_RouterThrowsOperationCanceledExceptionWithCancelledToken_Returns499WithoutThrowing()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceledException = new OperationCanceledException("User cancelled", cts.Token);
        router.RouteAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>())
              .Throws(canceledException);

        var result = await CompleteEndpoints.CompleteAsync(
            SampleRequest, router, loggerFactory, new DefaultHttpContext(), cts.Token);

        result.Should().BeOfType<StatusCodeHttpResult>();
        var statusCodeResult = (StatusCodeHttpResult)result;
        statusCodeResult.StatusCode.Should().Be(499);
    }

    [Fact]
    public async Task CompleteDualAsync_RouterThrowsTaskCanceledExceptionWithNonCancelledToken_Returns504()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);

        var nonCancelledToken = new CancellationToken(canceled: false);
        router.RouteDualAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>())
              .Throws(new TaskCanceledException("timeout"));

        var result = await CompleteEndpoints.CompleteDualAsync(
            SampleRequest, router, loggerFactory, new DefaultHttpContext(), nonCancelledToken);

        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result;
        problemResult.StatusCode.Should().Be(StatusCodes.Status504GatewayTimeout);
    }

    [Fact]
    public async Task CompleteDualAsync_RouterThrowsOperationCanceledExceptionWithCancelledToken_Returns499WithoutThrowing()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceledException = new OperationCanceledException("User cancelled", cts.Token);
        router.RouteDualAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>())
              .Throws(canceledException);

        var result = await CompleteEndpoints.CompleteDualAsync(
            SampleRequest, router, loggerFactory, new DefaultHttpContext(), cts.Token);

        result.Should().BeOfType<StatusCodeHttpResult>();
        var statusCodeResult = (StatusCodeHttpResult)result;
        statusCodeResult.StatusCode.Should().Be(499);
    }

    [Fact]
    public async Task CompleteAsync_SuccessfulRouterCall_Returns200WithResponse()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);

        var expectedResponse = BuildSampleResponse();
        router.RouteAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>())
              .Returns(expectedResponse);

        var result = await CompleteEndpoints.CompleteAsync(
            SampleRequest, router, loggerFactory, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeOfType<Ok<CompleteResponse>>();
        var okResult = (Ok<CompleteResponse>)result;
        okResult.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task CompleteDualAsync_SuccessfulRouterCall_Returns200WithDualResponse()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);

        var expectedResponse = BuildDualResponse();
        router.RouteDualAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>())
              .Returns(expectedResponse);

        var result = await CompleteEndpoints.CompleteDualAsync(
            SampleRequest, router, loggerFactory, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeOfType<Ok<DualCompleteResponse>>();
        var okResult = (Ok<DualCompleteResponse>)result;
        okResult.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task CompleteAsync_RouterThrowsFoundryCapacityExceeded_Returns429WithRetryAfterHeader()
    {
        const string ExpectedDeployment = "grok-4.3";
        const int ExpectedMaxInFlight = 10;
        const string ExpectedRetryAfter = "5";
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
        var httpContext = new DefaultHttpContext();

        router.RouteAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>())
              .Throws(new FoundryCapacityExceededException(ExpectedDeployment, ExpectedMaxInFlight));

        var result = await CompleteEndpoints.CompleteAsync(
            SampleRequest, router, loggerFactory, httpContext, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result;
        problemResult.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        httpContext.Response.Headers.RetryAfter.ToString().Should().Be(ExpectedRetryAfter);
    }

    [Fact]
    public async Task CompleteDualAsync_RouterThrowsFoundryCapacityExceeded_Returns429WithRetryAfterHeader()
    {
        const string ExpectedDeployment = "DeepSeek-V4-Pro";
        const int ExpectedMaxInFlight = 10;
        const string ExpectedRetryAfter = "5";
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
        var httpContext = new DefaultHttpContext();

        router.RouteDualAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>())
              .Throws(new FoundryCapacityExceededException(ExpectedDeployment, ExpectedMaxInFlight));

        var result = await CompleteEndpoints.CompleteDualAsync(
            SampleRequest, router, loggerFactory, httpContext, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result;
        problemResult.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        httpContext.Response.Headers.RetryAfter.ToString().Should().Be(ExpectedRetryAfter);
    }

    [Fact]
    public async Task CompleteAsync_NullPrompt_Returns422WithoutCallingRouter()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
        var nullPromptRequest = new CompleteRequest("task", null!, null, null);

        var result = await CompleteEndpoints.CompleteAsync(
            nullPromptRequest, router, loggerFactory, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result;
        problemResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        _ = router.DidNotReceive().RouteAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_WhitespacePrompt_Returns422WithoutCallingRouter()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
        var whitespacePromptRequest = new CompleteRequest("task", "   ", null, null);

        var result = await CompleteEndpoints.CompleteAsync(
            whitespacePromptRequest, router, loggerFactory, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result;
        problemResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        _ = router.DidNotReceive().RouteAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteDualAsync_NullPrompt_Returns422WithoutCallingRouter()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
        var nullPromptRequest = new CompleteRequest("task", null!, null, null);

        var result = await CompleteEndpoints.CompleteDualAsync(
            nullPromptRequest, router, loggerFactory, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result;
        problemResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        _ = router.DidNotReceive().RouteDualAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteDualAsync_WhitespacePrompt_Returns422WithoutCallingRouter()
    {
        var router = Substitute.For<IProviderRouter>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
        var whitespacePromptRequest = new CompleteRequest("task", "   ", null, null);

        var result = await CompleteEndpoints.CompleteDualAsync(
            whitespacePromptRequest, router, loggerFactory, new DefaultHttpContext(), CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result;
        problemResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        _ = router.DidNotReceive().RouteDualAsync(Arg.Any<CompleteRequest>(), Arg.Any<CancellationToken>());
    }
}
