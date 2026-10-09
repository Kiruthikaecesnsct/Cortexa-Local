using System.Net;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Ai;

public sealed class GeminiClientFactoryTests
{
    private static GeminiClientFactory Factory() =>
        new(new StubHttpClientFactory(StubHttpHandler.Returning(HttpStatusCode.OK, "{}")),
            MsOptions.Create(new GeminiProviderOptions { MaxRetries = 0 }));

    [Fact]
    public void GetOrCreate_SameKeyId_ReturnsTheCachedClient()
    {
        using var factory = Factory();
        var keys = new[] { new GeminiKey("key-a", "value-a") };

        var first = factory.GetOrCreate(keys, "key-a");
        var second = factory.GetOrCreate(keys, "key-a");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_KeyRemovedFromTheCurrentList_DoesNotKeepServingTheStaleClientForThatId()
    {
        using var factory = Factory();
        var keyA = new GeminiKey("key-a", "value-a");
        var keyB = new GeminiKey("key-b", "value-b");
        var originalClientForA = factory.GetOrCreate([keyA, keyB], "key-a");

        factory.GetOrCreate([keyB], "key-b");

        var replacementKeyA = new GeminiKey("key-a", "value-a-replacement");
        var rebuiltClientForA = factory.GetOrCreate([replacementKeyA, keyB], "key-a");

        Assert.NotSame(originalClientForA, rebuiltClientForA);
    }
}
