using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Presentation.Services;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Presentation;

public sealed class ProviderReadinessTests
{
    [Theory]
    [InlineData(false, "", "", false)]
    [InlineData(false, "", "us-central1", false)]
    [InlineData(false, "proj", "", false)]
    [InlineData(false, "proj", "us-central1", false)]
    [InlineData(true, "", "", false)]
    [InlineData(true, "", "us-central1", false)]
    [InlineData(true, "proj", "", false)]
    [InlineData(true, "proj", "us-central1", true)]
    public async Task CheckAsync_Vertex_ReadyOnlyWhenServiceAccountProjectAndLocationAreAllPresent(
        bool hasServiceAccount,
        string projectId,
        string location,
        bool expectedReady)
    {
        var credentials = new FakeVertexCredentialStore
        {
            ServiceAccountJson = hasServiceAccount ? "{\"type\":\"service_account\"}" : null,
        };
        var state = new FakeVertexSettingsState();
        state.Update(new VertexUserSettings(projectId, location));

        var readiness = new ProviderReadiness(
            new SettingsService(new FakeUserSettingsStore(), new InMemorySecretStore(), new FakeGeminiKeyStore()),
            new StubBedrockSso(),
            credentials,
            state,
            NullLogger<ProviderReadiness>.Instance);

        var result = await readiness.CheckAsync(CollectorProvider.Vertex, TestSupport.Ct);

        Assert.Equal(
            expectedReady ? ProviderReadinessState.Ready : ProviderReadinessState.Missing,
            result);
    }

    [Fact]
    public async Task CheckAsync_Vertex_CredentialStoreThrows_IsUnknown()
    {
        var state = new FakeVertexSettingsState();

        var readiness = new ProviderReadiness(
            new SettingsService(new FakeUserSettingsStore(), new InMemorySecretStore(), new FakeGeminiKeyStore()),
            new StubBedrockSso(),
            new ThrowingVertexCredentialStore(),
            state,
            NullLogger<ProviderReadiness>.Instance);

        var result = await readiness.CheckAsync(CollectorProvider.Vertex, TestSupport.Ct);

        Assert.Equal(ProviderReadinessState.Unknown, result);
    }

    private sealed class ThrowingVertexCredentialStore : Collector.Application.Ports.IVertexCredentialStore
    {
        public Task<string?> GetServiceAccountJsonAsync(CancellationToken cancellationToken) =>
            throw new SecretStoreException("unreachable");

        public Task SaveServiceAccountJsonAsync(string serviceAccountJson, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
