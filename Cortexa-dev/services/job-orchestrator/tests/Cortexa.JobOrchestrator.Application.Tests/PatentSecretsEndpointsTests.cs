using System.Reflection;
using System.Security.Claims;
using Cortexa.JobOrchestrator.Api.Contracts;
using Cortexa.JobOrchestrator.Api.Endpoints;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class PatentSecretsEndpointsTests
{
    private const string SecretCanary = "super-secret-value-must-never-leak-1234";

    private static readonly MethodInfo HandleWriteSecretMethod = typeof(PatentSecretsEndpoints)
        .GetMethod("HandleWriteSecret", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo HandleTestConnectionMethod = typeof(PatentSecretsEndpoints)
        .GetMethod("HandleTestConnection", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static PatentSourceConfig BuildConfig(int version = 1) =>
        new(new PatentSourceFlag(true), new PatentSourceFlag(true), new PatentSourceFlag(true), version, DateTimeOffset.UtcNow, "alpha@example.com");

    private static HttpContext BuildAuthenticatedContext(string userName = "operator@example.com")
    {
        var ctx = new DefaultHttpContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "test");
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    [Fact]
    public async Task HandleWriteSecret_UnknownSource_ReturnsValidationErrorWithoutTouchingSecretWriter()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        var secretWriter = Substitute.For<IPatentSecretWriter>();

        var result = await InvokeWrite("google-patents", new PatentSecretRequest(SecretCanary, null, null), repository, secretWriter);

        result.Should().BeOfType<BadRequest<ApiResponse<object>>>();
        await secretWriter.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, default);
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task HandleWriteSecret_UsptoValidKey_WritesOnlyTheUsptoSecretName()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns(BuildConfig(version: 2));
        repository.UpdateAsync(Arg.Any<PatentSourceConfig>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ((PatentSourceConfig)callInfo[0]) with { ConfigVersion = 3 });
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        secretWriter.SecretExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await InvokeWrite("uspto", new PatentSecretRequest(SecretCanary, null, null), repository, secretWriter);

        result.Should().BeOfType<Ok<ApiResponse<PatentConfigResponse>>>();
        await secretWriter.Received(1).SetSecretAsync(PatentSecretNames.UsptoApiKey, SecretCanary, Arg.Any<CancellationToken>());
        await secretWriter.DidNotReceive().SetSecretAsync(PatentSecretNames.EpoConsumerKey, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await secretWriter.DidNotReceive().SetSecretAsync(PatentSecretNames.LensApiKey, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await repository.Received(1).UpdateAsync(Arg.Any<PatentSourceConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleWriteSecret_EpoValidPair_WritesBothEpoSecretNames()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns(BuildConfig());
        repository.UpdateAsync(Arg.Any<PatentSourceConfig>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (PatentSourceConfig)callInfo[0]);
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        secretWriter.SecretExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await InvokeWrite("epo", new PatentSecretRequest(null, SecretCanary, SecretCanary), repository, secretWriter);

        result.Should().BeOfType<Ok<ApiResponse<PatentConfigResponse>>>();
        await secretWriter.Received(1).SetSecretAsync(PatentSecretNames.EpoConsumerKey, SecretCanary, Arg.Any<CancellationToken>());
        await secretWriter.Received(1).SetSecretAsync(PatentSecretNames.EpoOAuthSecret, SecretCanary, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleWriteSecret_InvalidKeyShape_ReturnsValidationErrorWithoutWriting()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        var secretWriter = Substitute.For<IPatentSecretWriter>();

        var result = await InvokeWrite("lens", new PatentSecretRequest("short", null, null), repository, secretWriter);

        var badRequest = result.Should().BeOfType<BadRequest<ApiResponse<object>>>().Subject;
        badRequest.Value!.ErrorCode.Should().Be("VALIDATION_ERROR");
        await secretWriter.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleWriteSecret_SecretStoreThrows_ReturnsServiceUnavailableWithoutLeakingSecretOrException()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        secretWriter.SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException($"failed for {SecretCanary}"));

        var result = await InvokeWrite("lens", new PatentSecretRequest(SecretCanary, null, null), repository, secretWriter);

        var json = result.Should().BeAssignableTo<IResult>().Subject;
        var serialized = await SerializeResultAsync(json);
        serialized.Should().NotContain(SecretCanary);
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task HandleWriteSecret_ResponseBody_NeverContainsTheSuppliedSecretValue()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns(BuildConfig());
        repository.UpdateAsync(Arg.Any<PatentSourceConfig>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (PatentSourceConfig)callInfo[0]);
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        secretWriter.SecretExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await InvokeWrite("uspto", new PatentSecretRequest(SecretCanary, null, null), repository, secretWriter);

        var serialized = await SerializeResultAsync(result);
        serialized.Should().NotContain(SecretCanary);
    }

    [Fact]
    public async Task HandleTestConnection_NoBodySupplied_UsesStoredCredentialPath()
    {
        var probe = Substitute.For<IPatentConnectionProbe>();
        probe.TestStoredAsync(PatentSource.Uspto, Arg.Any<CancellationToken>()).Returns(PatentProbeResult.Succeeded());

        var result = await InvokeTest("uspto", null, probe);

        result.Should().BeOfType<Ok<ApiResponse<PatentProbeResponse>>>();
        await probe.Received(1).TestStoredAsync(PatentSource.Uspto, Arg.Any<CancellationToken>());
        await probe.DidNotReceiveWithAnyArgs().TestSuppliedAsync(default, default!, default);
    }

    [Fact]
    public async Task HandleTestConnection_BodySupplied_UsesSuppliedCredentialPath()
    {
        var probe = Substitute.For<IPatentConnectionProbe>();
        probe.TestSuppliedAsync(PatentSource.Lens, Arg.Any<PatentSecretMaterial>(), Arg.Any<CancellationToken>())
            .Returns(PatentProbeResult.Succeeded());

        var result = await InvokeTest("lens", new PatentSecretRequest(SecretCanary, null, null), probe);

        result.Should().BeOfType<Ok<ApiResponse<PatentProbeResponse>>>();
        await probe.Received(1).TestSuppliedAsync(PatentSource.Lens, Arg.Any<PatentSecretMaterial>(), Arg.Any<CancellationToken>());
        await probe.DidNotReceiveWithAnyArgs().TestStoredAsync(default, default);
    }

    [Fact]
    public async Task HandleTestConnection_SuppliedInvalidShape_ReturnsValidationErrorWithoutProbing()
    {
        var probe = Substitute.For<IPatentConnectionProbe>();

        var result = await InvokeTest("uspto", new PatentSecretRequest("short", null, null), probe);

        var badRequest = result.Should().BeOfType<BadRequest<ApiResponse<object>>>().Subject;
        badRequest.Value!.ErrorCode.Should().Be("VALIDATION_ERROR");
        await probe.DidNotReceiveWithAnyArgs().TestSuppliedAsync(default, default!, default);
    }

    [Fact]
    public async Task HandleTestConnection_ResponseBody_NeverContainsTheSuppliedSecretValue()
    {
        var probe = Substitute.For<IPatentConnectionProbe>();
        probe.TestSuppliedAsync(PatentSource.Uspto, Arg.Any<PatentSecretMaterial>(), Arg.Any<CancellationToken>())
            .Returns(PatentProbeResult.Failed("unauthorized"));

        var result = await InvokeTest("uspto", new PatentSecretRequest(SecretCanary, null, null), probe);

        var serialized = await SerializeResultAsync(result);
        serialized.Should().NotContain(SecretCanary);
    }

    [Fact]
    public async Task HandleTestConnection_UnknownSource_ReturnsValidationError()
    {
        var probe = Substitute.For<IPatentConnectionProbe>();

        var result = await InvokeTest("not-a-source", null, probe);

        result.Should().BeOfType<BadRequest<ApiResponse<object>>>();
        await probe.DidNotReceiveWithAnyArgs().TestStoredAsync(default, default);
    }

    private static Task<string> SerializeResultAsync(IResult result)
    {
        var value = (result as Microsoft.AspNetCore.Http.IValueHttpResult)?.Value;
        return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(value));
    }

    private static async Task<IResult> InvokeWrite(
        string source,
        PatentSecretRequest request,
        IPatentSourceConfigRepository repository,
        IPatentSecretWriter secretWriter)
    {
        var ctx = BuildAuthenticatedContext();
        var invocationResult = HandleWriteSecretMethod.Invoke(null, [source, request, repository, secretWriter, ctx, CancellationToken.None]);
        return await (Task<IResult>)invocationResult!;
    }

    private static async Task<IResult> InvokeTest(string source, PatentSecretRequest? request, IPatentConnectionProbe probe)
    {
        var ctx = BuildAuthenticatedContext();
        var invocationResult = HandleTestConnectionMethod.Invoke(null, [source, request, probe, ctx, CancellationToken.None]);
        return await (Task<IResult>)invocationResult!;
    }
}
