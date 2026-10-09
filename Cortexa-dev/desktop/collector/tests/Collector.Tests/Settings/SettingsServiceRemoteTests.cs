using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Settings;

public class SettingsServiceRemoteTests
{
    private const string Token = "ghp_token";
    private const string Organization = "my-org";

    private readonly FakeUserSettingsStore _store = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly SettingsService _service;

    public SettingsServiceRemoteTests()
    {
        _service = new SettingsService(_store, _secrets, new FakeGeminiKeyStore());
    }

    [Fact]
    public async Task HasSecretAsync_NothingStored_ReturnsFalse()
    {
        Assert.False(await _service.HasSecretAsync(SecretSlot.GitHubPat, TestSupport.Ct));
    }

    [Fact]
    public async Task SetSecretAsync_PaddedValue_StoresTrimmedValueAndMarksPresent()
    {
        await _service.SetSecretAsync(SecretSlot.GitHubPat, $"  {Token}\r\n", TestSupport.Ct);

        Assert.True(await _service.HasSecretAsync(SecretSlot.GitHubPat, TestSupport.Ct));
        Assert.Equal(Token, _secrets.Values[SecretSlot.GitHubPat]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("tab\tinside")]
    public async Task SetSecretAsync_InvalidValue_ThrowsAndStoresNothing(string value)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetSecretAsync(SecretSlot.AzureDevOpsPat, value, TestSupport.Ct));

        Assert.Empty(_secrets.Values);
    }

    [Fact]
    public async Task SetSecretAsync_ValueTooLong_ThrowsAndStoresNothing()
    {
        var value = new string('a', SecretInputRules.MaxLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetSecretAsync(SecretSlot.GitHubPat, value, TestSupport.Ct));

        Assert.Empty(_secrets.Values);
    }

    [Fact]
    public async Task SetSecretAsync_ValueAtMaxLength_IsStored()
    {
        var value = new string('a', SecretInputRules.MaxLength);

        await _service.SetSecretAsync(SecretSlot.GitHubPat, value, TestSupport.Ct);

        Assert.Equal(value, _secrets.Values[SecretSlot.GitHubPat]);
    }

    [Fact]
    public async Task ClearSecretAsync_StoredSecret_RemovesOnlyThatSlot()
    {
        await _service.SetSecretAsync(SecretSlot.GitHubPat, Token, TestSupport.Ct);
        await _service.SetSecretAsync(SecretSlot.AzureDevOpsPat, "other", TestSupport.Ct);

        await _service.ClearSecretAsync(SecretSlot.GitHubPat, TestSupport.Ct);

        Assert.False(await _service.HasSecretAsync(SecretSlot.GitHubPat, TestSupport.Ct));
        Assert.True(await _service.HasSecretAsync(SecretSlot.AzureDevOpsPat, TestSupport.Ct));
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_ValidOrganization_SavesTrimmedValue()
    {
        var result = await _service.SaveRemoteSourcesAsync(new RemoteSourceSettings($"  {Organization} "), TestSupport.Ct);

        Assert.True(result.IsValid);
        Assert.Equal(new RemoteSourceSettings(Organization), _store.Remote);
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_EmptyOrganization_IsAcceptedAsCleared()
    {
        await _service.SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        var result = await _service.SaveRemoteSourcesAsync(new RemoteSourceSettings("   "), TestSupport.Ct);

        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, _store.Remote.AzureDevOpsOrganization);
    }

    [Theory]
    [InlineData("bad org")]
    [InlineData("-leading")]
    [InlineData("under_score")]
    [InlineData("slash/org")]
    [InlineData("org?x=1")]
    public async Task SaveRemoteSourcesAsync_InvalidOrganization_ReturnsFieldErrorAndKeepsPreviousValue(string organization)
    {
        await _service.SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        var result = await _service.SaveRemoteSourcesAsync(new RemoteSourceSettings(organization), TestSupport.Ct);

        Assert.Equal(EndpointField.AzureDevOpsOrganization, result.Field);
        Assert.Equal(RemoteSourceRules.OrganizationInvalidReason, result.Reason);
        Assert.Equal(Organization, _store.Remote.AzureDevOpsOrganization);
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_OrganizationOverFiftyCharacters_IsRejected()
    {
        const int MaxOrganizationLength = 50;
        var tooLong = new string('a', MaxOrganizationLength + 1);

        var result = await _service.SaveRemoteSourcesAsync(new RemoteSourceSettings(tooLong), TestSupport.Ct);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_OrganizationAtFiftyCharacters_IsAccepted()
    {
        const int MaxOrganizationLength = 50;
        var longest = new string('a', MaxOrganizationLength);

        var result = await _service.SaveRemoteSourcesAsync(new RemoteSourceSettings(longest), TestSupport.Ct);

        Assert.True(result.IsValid);
    }
}
