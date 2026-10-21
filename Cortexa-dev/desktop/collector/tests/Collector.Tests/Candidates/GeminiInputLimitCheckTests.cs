using Collector.Application.Candidates;
using Collector.Application.Ports;
using Collector.Tests.Support;

namespace Collector.Tests.Candidates;

public sealed class GeminiInputLimitCheckTests
{
    private const int Limit = 1000;

    private static readonly AiRequest Request = new()
    {
        SystemText = "system",
        UserText = "user",
        OutputSchemaJson = "{}",
    };

    [Theory]
    [InlineData(0, true)]
    [InlineData(Limit - 1, true)]
    [InlineData(Limit, true)]
    [InlineData(Limit + 1, false)]
    public async Task CheckAsync_ComparesCountStrictlyAgainstLimit(int count, bool within)
    {
        var check = new GeminiInputLimitCheck(new FakeInputBudget(count, Limit));

        var result = await check.CheckAsync(Request, TestSupport.Ct);

        Assert.Equal(within, result.IsWithin);
        Assert.Equal(count, result.TokenCount);
        Assert.Equal(Limit, result.Limit);
    }

    [Fact]
    public async Task CheckAsync_CountFails_PropagatesException()
    {
        var failure = new AiProviderException(AiFailureKind.MissingApiKey, "no key");
        var check = new GeminiInputLimitCheck(new FakeInputBudget(0, Limit) { Failure = failure });

        var thrown = await Assert.ThrowsAsync<AiProviderException>(() => check.CheckAsync(Request, TestSupport.Ct));

        Assert.Same(failure, thrown);
    }
}
