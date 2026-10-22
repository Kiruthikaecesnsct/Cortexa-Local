using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;

namespace Collector.Tests.Ai;

public sealed class GeminiQuotaHintParserTests
{
    private const double NoSeconds = -1;

    [Theory]
    [InlineData("Quota exceeded. Please retry in 5s.", 5, AiQuotaScope.Unknown)]
    [InlineData("Please retry after 12.5s", 12.5, AiQuotaScope.Unknown)]
    [InlineData("{\"retryDelay\": \"30s\"}", 30, AiQuotaScope.Unknown)]
    [InlineData("GenerateRequestsPerMinutePerProjectPerModel exceeded. Please retry in 7s.", 7, AiQuotaScope.PerMinute)]
    [InlineData("GenerateRequestsPerDayPerProjectPerModel exceeded. Please retry in 7s.", 7, AiQuotaScope.PerDay)]
    [InlineData("GenerateRequestsPerMinute limit reached", NoSeconds, AiQuotaScope.PerMinute)]
    [InlineData("GenerateRequestsPerDay limit reached", NoSeconds, AiQuotaScope.PerDay)]
    [InlineData("PerMinute and PerDay both exceeded", NoSeconds, AiQuotaScope.PerDay)]
    public void Parse_HintPresent_ReturnsRetryAfterAndScope(string message, double seconds, AiQuotaScope scope)
    {
        var hint = GeminiQuotaHintParser.Parse(message);

        Assert.NotNull(hint);
        Assert.Equal(seconds == NoSeconds ? null : TimeSpan.FromSeconds(seconds), hint.RetryAfter);
        Assert.Equal(scope, hint.Scope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Something else went wrong")]
    public void Parse_NoHint_ReturnsNull(string? message)
    {
        Assert.Null(GeminiQuotaHintParser.Parse(message));
    }
}

public sealed class GeminiQuotaWaitTests
{
    private const double NoSeconds = -1;
    private const int DefaultMax = 60;

    private static GeminiQuotaWait Wait(int maxSeconds) =>
        new(Microsoft.Extensions.Options.Options.Create(new GeminiProviderOptions { MaxQuotaRetryDelaySeconds = maxSeconds }), TimeProvider.System);

    private static AiProviderException Failure(double retrySeconds, AiQuotaScope scope) =>
        new(AiFailureKind.QuotaExceeded, "quota")
        {
            QuotaHint = new AiQuotaHint(retrySeconds == NoSeconds ? null : TimeSpan.FromSeconds(retrySeconds), scope),
        };

    [Theory]
    [InlineData(5, AiQuotaScope.Unknown, 5)]
    [InlineData(DefaultMax, AiQuotaScope.Unknown, DefaultMax)]
    [InlineData(10, AiQuotaScope.PerMinute, 10)]
    [InlineData(NoSeconds, AiQuotaScope.PerMinute, DefaultMax)]
    [InlineData(120, AiQuotaScope.PerMinute, NoSeconds)]
    [InlineData(120, AiQuotaScope.Unknown, NoSeconds)]
    [InlineData(NoSeconds, AiQuotaScope.Unknown, NoSeconds)]
    [InlineData(5, AiQuotaScope.PerDay, NoSeconds)]
    [InlineData(NoSeconds, AiQuotaScope.PerDay, NoSeconds)]
    public void DelayFor_HintAndScope_ReturnsExpectedDelay(double retrySeconds, AiQuotaScope scope, double expectedSeconds)
    {
        var delay = Wait(DefaultMax).DelayFor(Failure(retrySeconds, scope));

        Assert.Equal(expectedSeconds == NoSeconds ? null : TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void DelayFor_NoHint_ReturnsNull()
    {
        Assert.Null(Wait(DefaultMax).DelayFor(new AiProviderException(AiFailureKind.QuotaExceeded, "quota")));
    }

    [Fact]
    public void DelayFor_NullFailure_ReturnsNull()
    {
        Assert.Null(Wait(DefaultMax).DelayFor(null));
    }

    [Theory]
    [InlineData(AiQuotaScope.PerMinute)]
    [InlineData(AiQuotaScope.Unknown)]
    public void DelayFor_MaxZero_ReturnsNull(AiQuotaScope scope)
    {
        Assert.Null(Wait(0).DelayFor(Failure(1, scope)));
    }
}
