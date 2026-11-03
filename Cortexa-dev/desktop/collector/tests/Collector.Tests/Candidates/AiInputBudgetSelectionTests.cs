using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Candidates;

public sealed class AiInputBudgetSelectionTests
{
    private const int VertexLimit = 777;
    private const int GeminiLimit = 5000;

    private static readonly AiRequest Request = new() { SystemText = "abc", UserText = "defg", OutputSchemaJson = "{}" };

    private static AiInputBudgetSelection Selection(Dictionary<CollectorProvider, int> limits) =>
        new(new FakeInputBudget(1, GeminiLimit), new LengthTokenCounter(), MsOptions.Create(new AiInputLimitOptions { MaxInputTokens = limits }));

    [Fact]
    public void Resolve_Gemini_ReturnsGeminiBudget()
    {
        var budget = Selection([]).Resolve(CollectorProvider.Gemini);

        Assert.Equal(GeminiLimit, budget.MaxInputTokens);
    }

    [Fact]
    public async Task Resolve_Vertex_UsesConfiguredLimitAndLocalCount()
    {
        var budget = Selection(new() { [CollectorProvider.Vertex] = VertexLimit }).Resolve(CollectorProvider.Vertex);

        Assert.Equal(VertexLimit, budget.MaxInputTokens);
        Assert.Equal(7, await budget.CountAsync(Request, TestSupport.Ct));
    }

    [Fact]
    public void Resolve_LimitMissing_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Selection([]).Resolve(CollectorProvider.Claude));

    [Fact]
    public void Validator_MissingProvider_Fails()
    {
        var options = new AiInputLimitOptions { MaxInputTokens = new() { [CollectorProvider.Claude] = 1 } };

        var result = new AiInputLimitOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    private sealed class LengthTokenCounter : ITokenCounter
    {
        public IReadOnlyList<int> Encode(string text) => [.. text.Select(c => (int)c)];

        public string Decode(IReadOnlyList<int> tokens) => string.Concat(tokens.Select(t => (char)t));

        public int Count(string text) => text.Length;
    }
}
