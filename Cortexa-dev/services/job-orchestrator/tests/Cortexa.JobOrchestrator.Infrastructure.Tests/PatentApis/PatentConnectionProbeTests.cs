using System.Net;
using System.Text;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.PatentApis;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests.PatentApis;

public sealed class PatentConnectionProbeTests
{
    private const string SecretCanary = "super-secret-value-must-never-leak";

    private static PatentConnectionProbe BuildProbe(StubHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var settings = Options.Create(new PatentApiSettings());
        return new PatentConnectionProbe(httpClient, secretClient: null, settings);
    }

    [Fact]
    public async Task TestSuppliedAsync_UsptoSuccessStatus_ReturnsSucceeded()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Uspto, new PatentSecretMaterial(SecretCanary, null, null), CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.LastRequest!.Headers.GetValues("X-API-KEY").Should().ContainSingle().Which.Should().Be(SecretCanary);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TestSuppliedAsync_UsptoAuthFailureStatus_ReturnsUnauthorized(HttpStatusCode statusCode)
    {
        var handler = new StubHandler(statusCode);
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Uspto, new PatentSecretMaterial(SecretCanary, null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("unauthorized");
    }

    [Fact]
    public async Task TestSuppliedAsync_UsptoServerError_ReturnsUnexpectedStatus()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError);
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Uspto, new PatentSecretMaterial(SecretCanary, null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("unexpected_status");
    }

    [Fact]
    public async Task TestSuppliedAsync_UsptoMissingKey_ReturnsMissingCredentialWithoutCallingHttp()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Uspto, new PatentSecretMaterial(null, null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("missing_credential");
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task TestSuppliedAsync_EpoBothFieldsSuccess_ReturnsSucceeded()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Epo, new PatentSecretMaterial(null, SecretCanary, SecretCanary), CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Basic");
    }

    [Fact]
    public async Task TestSuppliedAsync_EpoMissingOauth_ReturnsMissingCredentialWithoutCallingHttp()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Epo, new PatentSecretMaterial(null, SecretCanary, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("missing_credential");
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task TestSuppliedAsync_LensSuccessStatus_ReturnsSucceeded()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Lens, new PatentSecretMaterial(SecretCanary, null, null), CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be(SecretCanary);
    }

    [Fact]
    public async Task TestSuppliedAsync_NetworkFailure_ReturnsNetworkErrorWithoutLeakingException()
    {
        var handler = new StubHandler(new HttpRequestException("connection refused"));
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Lens, new PatentSecretMaterial(SecretCanary, null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("network_error");
        result.FailureReason.Should().NotContain(SecretCanary);
    }

    [Fact]
    public async Task TestSuppliedAsync_Timeout_ReturnsTimeoutReason()
    {
        var handler = new StubHandler(new TaskCanceledException("timed out"));
        var probe = BuildProbe(handler);

        var result = await probe.TestSuppliedAsync(PatentSource.Lens, new PatentSecretMaterial(SecretCanary, null, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("timeout");
    }

    [Fact]
    public async Task TestStoredAsync_NoSecretClientConfigured_ReturnsKeyVaultUnavailableWithoutCallingHttp()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var probe = BuildProbe(handler);

        var result = await probe.TestStoredAsync(PatentSource.Uspto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("key_vault_unavailable");
        handler.LastRequest.Should().BeNull();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _statusCode;
        private readonly Exception? _exception;

        public StubHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public StubHandler(Exception exception)
        {
            _exception = exception;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            if (_exception is not null)
                return Task.FromException<HttpResponseMessage>(_exception);

            var response = new HttpResponseMessage(_statusCode!.Value)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
