using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class AnthropicClientFactoryTests
{
    private const string FirstKey = "sk-first-key-value";
    private const string SecondKey = "sk-second-key-value";

    private readonly InMemorySecretStore _secrets = new();

    private AnthropicClientFactory Factory() =>
        new(_secrets, Microsoft.Extensions.Options.Options.Create(new AiProviderOptions()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_NoKey_ThrowsMissingApiKeyWithFixedMessage(string? stored)
    {
        if (stored is not null)
        {
            _secrets.Values[SecretSlot.AnthropicApiKey] = stored;
        }

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => Factory().CreateAsync(TestSupport.Ct));

        Assert.Equal(AiFailureKind.MissingApiKey, exception.Kind);
        Assert.Equal(AnthropicClientFactory.MissingKeyMessage, exception.Message);
    }

    [Fact]
    public async Task CreateAsync_SameKeyTwice_ReturnsCachedClient()
    {
        _secrets.Values[SecretSlot.AnthropicApiKey] = FirstKey;
        var factory = Factory();

        var first = await factory.CreateAsync(TestSupport.Ct);
        var second = await factory.CreateAsync(TestSupport.Ct);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task CreateAsync_KeyChanged_CreatesNewClient()
    {
        _secrets.Values[SecretSlot.AnthropicApiKey] = FirstKey;
        var factory = Factory();
        var first = await factory.CreateAsync(TestSupport.Ct);

        _secrets.Values[SecretSlot.AnthropicApiKey] = SecondKey;
        var second = await factory.CreateAsync(TestSupport.Ct);

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task CreateAsync_KeyRemovedAfterUse_ThrowsWithoutLeakingOldKey()
    {
        _secrets.Values[SecretSlot.AnthropicApiKey] = FirstKey;
        var factory = Factory();
        await factory.CreateAsync(TestSupport.Ct);

        _secrets.Values.Remove(SecretSlot.AnthropicApiKey);
        var exception = await Assert.ThrowsAsync<AiProviderException>(() => factory.CreateAsync(TestSupport.Ct));

        Assert.DoesNotContain(FirstKey, exception.ToString(), StringComparison.Ordinal);
    }
}
