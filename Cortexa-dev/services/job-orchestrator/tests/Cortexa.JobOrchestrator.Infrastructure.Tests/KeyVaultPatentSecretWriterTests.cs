using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests;

public sealed class KeyVaultPatentSecretWriterTests
{
    [Theory]
    [InlineData("uspto-api-key")]
    [InlineData("epo-consumer-key")]
    [InlineData("epo-oauth-secret")]
    [InlineData("lens-api-key")]
    public async Task SetSecretAsync_AllowedNameWithNoClient_ThrowsKeyVaultUnavailable(string secretName)
    {
        var writer = new KeyVaultPatentSecretWriter(null);

        var act = async () => await writer.SetSecretAsync(secretName, "value", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("uspto-secret")]
    [InlineData("git-pat-anything")]
    [InlineData("../uspto-api-key")]
    [InlineData("")]
    public async Task SetSecretAsync_DisallowedName_ThrowsArgumentExceptionWithoutTouchingKeyVault(string secretName)
    {
        var writer = new KeyVaultPatentSecretWriter(null);

        var act = async () => await writer.SetSecretAsync(secretName, "value", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("uspto-secret")]
    [InlineData("git-pat-anything")]
    public async Task SecretExistsAsync_DisallowedName_ThrowsArgumentException(string secretName)
    {
        var writer = new KeyVaultPatentSecretWriter(null);

        var act = async () => await writer.SecretExistsAsync(secretName, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SecretExistsAsync_AllowedNameWithNoClient_ReturnsFalseWithoutThrowing()
    {
        var writer = new KeyVaultPatentSecretWriter(null);

        var exists = await writer.SecretExistsAsync(PatentSecretNames.UsptoApiKey, CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public void AllowedSecretNames_MatchTheFixedFourNames()
    {
        PatentSecretNames.All.Should().BeEquivalentTo(
        [
            "uspto-api-key",
            "epo-consumer-key",
            "epo-oauth-secret",
            "lens-api-key"
        ]);
    }
}
