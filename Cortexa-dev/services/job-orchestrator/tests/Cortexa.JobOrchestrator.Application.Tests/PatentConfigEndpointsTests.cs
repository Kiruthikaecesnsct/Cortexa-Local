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

public sealed class PatentConfigEndpointsTests
{
    private const string SecretCanary = "super-secret-value-must-never-leak";

    private static readonly MethodInfo HandleGetMethod = typeof(PatentConfigEndpoints)
        .GetMethod("HandleGetPatentConfig", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo HandlePutMethod = typeof(PatentConfigEndpoints)
        .GetMethod("HandlePutPatentConfig", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static PatentSourceConfig BuildConfig(bool uspto = true, bool epo = true, bool lens = true, int version = 1) =>
        new(new PatentSourceFlag(uspto), new PatentSourceFlag(epo), new PatentSourceFlag(lens), version, DateTimeOffset.UtcNow, "alpha@example.com");

    private static HttpContext BuildAuthenticatedContext(string userName = "alpha@example.com")
    {
        var ctx = new DefaultHttpContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "test");
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    [Fact]
    public async Task HandleGet_NoCredentialsStored_ReturnsNotSetForAllSources()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns(BuildConfig());
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        secretWriter.SecretExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await InvokeGet(repository, secretWriter);

        var ok = result.Should().BeOfType<Ok<ApiResponse<PatentConfigResponse>>>().Subject;
        var response = ok.Value!.Data!;
        response.UsptoCredentialStatus.Should().Be("not_set");
        response.EpoCredentialStatus.Consumer.Should().Be("not_set");
        response.EpoCredentialStatus.Oauth.Should().Be("not_set");
        response.LensCredentialStatus.Should().Be("not_set");
    }

    [Fact]
    public async Task HandleGet_AllCredentialsStored_ReturnsSetForAllSources()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns(BuildConfig());
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        secretWriter.SecretExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await InvokeGet(repository, secretWriter);

        var ok = result.Should().BeOfType<Ok<ApiResponse<PatentConfigResponse>>>().Subject;
        var response = ok.Value!.Data!;
        response.UsptoCredentialStatus.Should().Be("set");
        response.EpoCredentialStatus.Consumer.Should().Be("set");
        response.EpoCredentialStatus.Oauth.Should().Be("set");
        response.LensCredentialStatus.Should().Be("set");
    }

    [Fact]
    public async Task HandleGet_NeverExposesAnySecretValue()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns(BuildConfig());
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        secretWriter.SecretExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await InvokeGet(repository, secretWriter);

        var ok = result.Should().BeOfType<Ok<ApiResponse<PatentConfigResponse>>>().Subject;
        var serialized = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        serialized.Should().NotContain(SecretCanary);
        await secretWriter.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandlePut_TogglesFlags_PersistsViaRepositoryWithCallerIdentity()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        repository.GetAsync(Arg.Any<CancellationToken>()).Returns(BuildConfig(uspto: true, epo: true, lens: true, version: 4));
        repository.UpdateAsync(Arg.Any<PatentSourceConfig>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ((PatentSourceConfig)callInfo[0]) with { ConfigVersion = 5 });
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        secretWriter.SecretExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var ctx = BuildAuthenticatedContext("operator@example.com");
        var request = new PatentConfigRequest(UsptoEnabled: false, EpoEnabled: true, LensEnabled: false);

        var result = await InvokePut(request, repository, secretWriter, ctx);

        var ok = result.Should().BeOfType<Ok<ApiResponse<PatentConfigResponse>>>().Subject;
        ok.Value!.Data!.UsptoEnabled.Should().BeFalse();
        ok.Value!.Data!.EpoEnabled.Should().BeTrue();
        ok.Value!.Data!.LensEnabled.Should().BeFalse();
        ok.Value!.Data!.ConfigVersion.Should().Be(5);

        await repository.Received(1).UpdateAsync(
            Arg.Is<PatentSourceConfig>(c => c.UpdatedBy == "operator@example.com" && !c.Uspto.Enabled && c.Epo.Enabled && !c.Lens.Enabled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandlePut_NullRequest_ReturnsValidationErrorWithoutCallingRepository()
    {
        var repository = Substitute.For<IPatentSourceConfigRepository>();
        var secretWriter = Substitute.For<IPatentSecretWriter>();
        var ctx = BuildAuthenticatedContext();

        var result = await InvokePut(null!, repository, secretWriter, ctx);

        var badRequest = result.Should().BeOfType<BadRequest<ApiResponse<object>>>().Subject;
        badRequest.Value!.ErrorCode.Should().Be("VALIDATION_ERROR");
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    private static async Task<IResult> InvokeGet(IPatentSourceConfigRepository repository, IPatentSecretWriter secretWriter)
    {
        var ctx = BuildAuthenticatedContext();
        var invocationResult = HandleGetMethod.Invoke(null, [repository, secretWriter, ctx, CancellationToken.None]);
        return await (Task<IResult>)invocationResult!;
    }

    private static async Task<IResult> InvokePut(
        PatentConfigRequest request,
        IPatentSourceConfigRepository repository,
        IPatentSecretWriter secretWriter,
        HttpContext ctx)
    {
        var invocationResult = HandlePutMethod.Invoke(null, [request, repository, secretWriter, ctx, CancellationToken.None]);
        return await (Task<IResult>)invocationResult!;
    }
}
