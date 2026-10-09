using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;

namespace Collector.Tests.Ai;

public sealed class GeminiKeyRotationTests
{
    private static GeminiKey Key(string id) => new(id, $"value-{id}-0000000000");

    [Fact]
    public void OrderFrom_EmptyKeys_ReturnsEmpty()
    {
        var result = GeminiKeyRotation.OrderFrom([], "missing");

        Assert.Empty(result);
    }

    [Fact]
    public void OrderFrom_NoActiveKeyId_ReturnsOriginalOrder()
    {
        GeminiKey[] keys = [Key("a"), Key("b"), Key("c")];

        var result = GeminiKeyRotation.OrderFrom(keys, null);

        Assert.Equal(["a", "b", "c"], result.Select(key => key.Id));
    }

    [Fact]
    public void OrderFrom_ActiveKeyIsFirst_ReturnsOriginalOrder()
    {
        GeminiKey[] keys = [Key("a"), Key("b"), Key("c")];

        var result = GeminiKeyRotation.OrderFrom(keys, "a");

        Assert.Equal(["a", "b", "c"], result.Select(key => key.Id));
    }

    [Fact]
    public void OrderFrom_ActiveKeyIsMiddle_StartsThereAndWrapsAround()
    {
        GeminiKey[] keys = [Key("a"), Key("b"), Key("c")];

        var result = GeminiKeyRotation.OrderFrom(keys, "b");

        Assert.Equal(["b", "c", "a"], result.Select(key => key.Id));
    }

    [Fact]
    public void OrderFrom_ActiveKeyIsLast_StartsThereAndWrapsAround()
    {
        GeminiKey[] keys = [Key("a"), Key("b"), Key("c")];

        var result = GeminiKeyRotation.OrderFrom(keys, "c");

        Assert.Equal(["c", "a", "b"], result.Select(key => key.Id));
    }

    [Fact]
    public void OrderFrom_ActiveKeyNotInList_FallsBackToOriginalOrder()
    {
        GeminiKey[] keys = [Key("a"), Key("b"), Key("c")];

        var result = GeminiKeyRotation.OrderFrom(keys, "missing");

        Assert.Equal(["a", "b", "c"], result.Select(key => key.Id));
    }
}
