using System.Text.Json;
using Cortexa.ModelRouter.Api.Endpoints;
using Cortexa.ModelRouter.Application.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cortexa.ModelRouter.Tests;

public sealed class CompleteEndpointsContentFilterTests
{
    private static DefaultHttpContext CreateTestHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ProblemDetailsFactory>(new TestProblemDetailsFactory());
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            Response = { Body = new MemoryStream() }
        };
        return httpContext;
    }

    private sealed class TestProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null)
        {
            return new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Type = type,
                Detail = detail,
                Instance = instance
            };
        }

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext,
            ModelStateDictionary modelStateDictionary,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null)
        {
            return new ValidationProblemDetails(modelStateDictionary)
            {
                Status = statusCode,
                Title = title,
                Type = type,
                Detail = detail,
                Instance = instance
            };
        }
    }

    [Fact]
    public async Task HandleModelProviderException_400WithContentFilter_Returns400WithCodeAndCategories()
    {
        var ex = new ModelProviderException(
            "Foundry",
            400,
            "Foundry returned 400",
            "content_filter",
            ["jailbreak", "violence"]);
        var httpContext = CreateTestHttpContext();
        var logger = NullLogger.Instance;

        var result = CompleteEndpoints.HandleModelProviderException(ex, httpContext, logger, "POST /complete");

        await result.ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.Should().Be(400);
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseBody);
        problemDetails.Should().NotBeNull();
        problemDetails!.Extensions.Should().ContainKey("code");
        ((JsonElement)problemDetails.Extensions["code"]!).GetString().Should().Be("content_filter");
        problemDetails.Extensions.Should().ContainKey("categories");
        ((JsonElement)problemDetails.Extensions["categories"]!).EnumerateArray()
            .Select(e => e.GetString()).Should().Contain(["jailbreak", "violence"]);
    }

    [Fact]
    public async Task HandleModelProviderException_400WithoutContentFilter_Returns400()
    {
        var ex = new ModelProviderException(
            "Foundry",
            400,
            "Foundry returned 400",
            "invalid_request",
            null);
        var httpContext = CreateTestHttpContext();
        var logger = NullLogger.Instance;

        var result = CompleteEndpoints.HandleModelProviderException(ex, httpContext, logger, "POST /complete");

        await result.ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task HandleModelProviderException_500_Returns502()
    {
        var ex = new ModelProviderException("Foundry", 500, "Foundry returned 500");
        var httpContext = CreateTestHttpContext();
        var logger = NullLogger.Instance;

        var result = CompleteEndpoints.HandleModelProviderException(ex, httpContext, logger, "POST /complete");

        await result.ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task HandleModelProviderException_NullStatusCode_Returns502()
    {
        var ex = new ModelProviderException("Foundry", null, "Unknown error");
        var httpContext = CreateTestHttpContext();
        var logger = NullLogger.Instance;

        var result = CompleteEndpoints.HandleModelProviderException(ex, httpContext, logger, "POST /complete");

        await result.ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task HandleModelProviderException_403_Returns403()
    {
        var ex = new ModelProviderException("Foundry", 403, "Foundry returned 403");
        var httpContext = CreateTestHttpContext();
        var logger = NullLogger.Instance;

        var result = CompleteEndpoints.HandleModelProviderException(ex, httpContext, logger, "POST /complete");

        await result.ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task HandleModelProviderException_422_Returns422()
    {
        var ex = new ModelProviderException("Foundry", 422, "Foundry returned 422");
        var httpContext = CreateTestHttpContext();
        var logger = NullLogger.Instance;

        var result = CompleteEndpoints.HandleModelProviderException(ex, httpContext, logger, "POST /complete");

        await result.ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.Should().Be(422);
    }
}
