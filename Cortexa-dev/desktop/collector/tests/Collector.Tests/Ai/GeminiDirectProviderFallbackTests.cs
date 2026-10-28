using System.Net;
using System.Text.RegularExpressions;
using Collector.Application.Ai;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Ai;

public sealed partial class GeminiDirectProviderFallbackTests
{
    private const string Primary = "gemini-primary";
    private const string Backup = "gemini-backup";
    private const string Third = "gemini-third";
    private const string FirstKeyValue = "first-key-value";
    private const string SecondKeyValue = "second-key-value";
    private const int TokenLimit = 1000;
    private const int RetrySeconds = 5;

    private static readonly GeminiKey FirstKey = new("key-first", FirstKeyValue);
    private static readonly GeminiKey SecondKey = new("key-second", SecondKeyValue);

    private readonly GeminiModelExhaustionTracker _tracker = new();

    private sealed record ModelCall(string Model, string Key, int Attempt);

    [GeneratedRegex("models/(?<model>[^:/]+):generateContent")]
    private static partial Regex GenerateContentPath();

    private static string ModelOf(CapturedRequest request) =>
        GenerateContentPath().Match(request.Uri!.AbsolutePath).Groups["model"].Value;

    private static StubHttpHandler Endpoint(Func<ModelCall, HttpResponseMessage> respond)
    {
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        return new StubHttpHandler((request, _) =>
        {
            var model = ModelOf(request);
            int attempt;
            lock (attempts)
            {
                attempt = attempts.GetValueOrDefault(model) + 1;
                attempts[model] = attempt;
            }

            return Task.FromResult(respond(new ModelCall(model, request.Headers["x-goog-api-key"], attempt)));
        });
    }

    private static HttpResponseMessage Ok(string model) =>
        StubHttpHandler.Json(HttpStatusCode.OK, GeminiWire.Response("STOP", model: model));

    private static HttpResponseMessage Status(HttpStatusCode status, string message = "upstream said no") =>
        StubHttpHandler.Json(
            status,
            "{\"error\":{\"code\":" + (int)status + ",\"message\":\"" + message + "\",\"status\":\"FAILED\"}}");

    private static HttpResponseMessage Quota(string message = "upstream said no") => Status(HttpStatusCode.TooManyRequests, message);

    private static int Requests(StubHttpHandler handler, string model) =>
        handler.Requests.Count(request => ModelOf(request) == model);

    private static GeminiProviderOptions Options(params string[] fallbacks) =>
        new() { Model = Primary, MaxRetries = 0, FallbackModels = [.. fallbacks] };

    private static FakeGeminiKeyStore TwoKeyStore()
    {
        var store = new FakeGeminiKeyStore();
        store.Keys.Add(FirstKey);
        store.Keys.Add(SecondKey);
        return store;
    }

    private GeminiDirectProvider Provider(StubHttpHandler handler, GeminiProviderOptions options, GeminiWireSetup? setup = null)
    {
        var wire = setup ?? new GeminiWireSetup();
        return GeminiWire.Provider(handler, options, wire with { Tracker = _tracker });
    }

    [Fact]
    public async Task CompleteAsync_PrimaryQuotaExceeded_FallsBackAndReportsFallbackModel()
    {
        var handler = Endpoint(call => call.Model == Primary ? Quota() : Ok(call.Model));

        var completion = await Provider(handler, Options(Backup)).CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(Backup, completion.Model);
        Assert.Equal(AiOutcome.Completed, completion.Outcome);
        Assert.Equal(1, Requests(handler, Primary));
        Assert.Equal(1, Requests(handler, Backup));
    }

    [Fact]
    public async Task CompleteAsync_ExhaustedModelInSameRun_GetsNoRequestOnLaterCalls()
    {
        var handler = Endpoint(call => call.Model == Primary ? Quota() : Ok(call.Model));
        var provider = Provider(handler, Options(Backup));
        await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        var second = await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(Backup, second.Model);
        Assert.Equal(1, Requests(handler, Primary));
        Assert.Equal(2, Requests(handler, Backup));
    }

    [Fact]
    public async Task CompleteAsync_AfterTrackerReset_ExhaustedModelIsTriedAgain()
    {
        var handler = Endpoint(call => call.Model == Primary ? Quota() : Ok(call.Model));
        var provider = Provider(handler, Options(Backup));
        await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        _tracker.Reset();
        await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(2, Requests(handler, Primary));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiFailureKind.Permanent)]
    [InlineData(HttpStatusCode.BadRequest, AiFailureKind.Permanent)]
    public async Task CompleteAsync_NonQuotaFailureOnPrimary_KeepsItsKindAndNeverTriesBackup(HttpStatusCode status, AiFailureKind expectedKind)
    {
        var handler = Endpoint(call => call.Model == Primary ? Status(status) : Ok(call.Model));

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Provider(handler, Options(Backup)).CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.Equal(0, Requests(handler, Backup));
        Assert.False(_tracker.IsExhausted(Primary));
    }

    [Fact]
    public async Task CompleteAsync_ServiceUnavailableOnPrimary_FallsBackWithoutMarkingItExhausted()
    {
        var handler = Endpoint(call => call.Model == Primary ? Status(HttpStatusCode.ServiceUnavailable) : Ok(call.Model));

        var completion = await Provider(handler, Options(Backup)).CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(Backup, completion.Model);
        Assert.False(_tracker.IsExhausted(Primary));
    }

    [Fact]
    public async Task CompleteAsync_ConnectionFailureOnPrimary_FallsBackToNextModel()
    {
        var requested = new List<string>();
        var handler = new StubHttpHandler((request, _) =>
        {
            requested.Add(ModelOf(request));
            if (ModelOf(request) == Primary)
            {
                throw new HttpRequestException("no route");
            }

            return Task.FromResult(Ok(ModelOf(request)));
        });

        var completion = await Provider(handler, Options(Backup)).CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(Backup, completion.Model);
        Assert.Equal([Primary, Backup], requested);
    }

    [Fact]
    public async Task CompleteAsync_EveryModelUnavailable_ThrowsTransientAfterTryingAll()
    {
        var handler = Endpoint(_ => Status(HttpStatusCode.ServiceUnavailable));

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Provider(handler, Options(Backup)).CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
        Assert.Equal(1, Requests(handler, Primary));
        Assert.Equal(1, Requests(handler, Backup));
    }

    [Fact]
    public async Task CompleteAsync_QuotaOnOneKeyAndRejectedOnOther_DoesNotFallBack()
    {
        var handler = Endpoint(call => call.Model != Primary ? Ok(call.Model)
            : call.Key == FirstKeyValue ? Quota() : Status(HttpStatusCode.Unauthorized));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { KeyStore = TwoKeyStore() });

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.Permanent, exception.Kind);
        Assert.Equal(0, Requests(handler, Backup));
        Assert.False(_tracker.IsExhausted(Primary));
    }

    [Fact]
    public async Task CompleteAsync_QuotaOnEveryKey_FallsBackToNextModel()
    {
        var handler = Endpoint(call => call.Model == Primary ? Quota() : Ok(call.Model));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { KeyStore = TwoKeyStore() });

        var completion = await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(Backup, completion.Model);
        Assert.Equal(2, Requests(handler, Primary));
    }

    [Fact]
    public async Task CompleteAsync_EveryModelOutOfQuota_ThrowsQuotaExceededAfterLastModel()
    {
        var handler = Endpoint(_ => Quota());

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Provider(handler, Options(Backup, Third)).CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.QuotaExceeded, exception.Kind);
        Assert.Equal(1, Requests(handler, Primary));
        Assert.Equal(1, Requests(handler, Backup));
        Assert.Equal(1, Requests(handler, Third));
        Assert.True(_tracker.IsExhausted(Third));
    }

    [Fact]
    public async Task CompleteAsync_EveryModelAlreadyExhausted_ThrowsQuotaExceededWithoutRequests()
    {
        var handler = Endpoint(call => Ok(call.Model));
        _tracker.MarkExhausted(Primary);
        _tracker.MarkExhausted(Backup);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Provider(handler, Options(Backup)).CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.QuotaExceeded, exception.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CompleteAsync_FallbackRecountOverLimit_ThrowsInputTooLargeAndSkipsThatModel()
    {
        var budget = new RecordingBudget(model => model == Backup ? TokenLimit + 1 : 1);
        var handler = Endpoint(call => call.Model == Primary ? Quota() : Ok(call.Model));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { Budget = budget });

        var exception = await Assert.ThrowsAsync<AiInputTooLargeException>(
            () => provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(TokenLimit + 1, exception.TokenCount);
        Assert.Equal(TokenLimit, exception.Limit);
        Assert.Equal(0, Requests(handler, Backup));
    }

    [Fact]
    public async Task CompleteAsync_PrimarySucceeds_NeverRecountsTokens()
    {
        var budget = new RecordingBudget(_ => 1);
        var handler = Endpoint(call => Ok(call.Model));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { Budget = budget });

        await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Empty(budget.Models);
    }

    [Fact]
    public async Task CompleteAsync_FallbackUsed_RecountsOnlyTheFallbackModel()
    {
        var budget = new RecordingBudget(_ => 1);
        var handler = Endpoint(call => call.Model == Primary ? Quota() : Ok(call.Model));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { Budget = budget });

        await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal([Backup], budget.Models);
    }

    [Fact]
    public async Task CompleteAsync_NoFallbackModels_BehavesAsBefore()
    {
        var handler = Endpoint(_ => Quota());
        var provider = Provider(handler, Options());

        var first = await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct));
        await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.QuotaExceeded, first.Kind);
        Assert.Equal(2, Requests(handler, Primary));
        Assert.False(_tracker.IsExhausted(Primary));
    }

    [Fact]
    public async Task CompleteAsync_QuotaDuringFallbackRecount_MovesToNextModelWithoutMarkingIt()
    {
        var budget = new RecordingBudget(model => model == Backup
            ? throw new AiProviderException(AiFailureKind.QuotaExceeded, "count quota")
            : 1);
        var handler = Endpoint(call => call.Model == Primary ? Quota() : Ok(call.Model));
        var provider = Provider(handler, Options(Backup, Third), new GeminiWireSetup { Budget = budget });

        var completion = await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(Third, completion.Model);
        Assert.Equal(0, Requests(handler, Backup));
        Assert.True(_tracker.IsExhausted(Primary));
        Assert.False(_tracker.IsExhausted(Backup));
    }

    [Fact]
    public async Task CompleteAsync_PerMinuteLimitWithShortDelay_WaitsOnceAndRetriesSameModel()
    {
        var time = new SignalingTimeProvider();
        var handler = Endpoint(call => call.Model == Primary && call.Attempt == 1 ? Quota(PerMinuteMessage) : Ok(call.Model));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { Time = time });

        var pending = provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);
        await time.TimerCreated.WaitAsync(TestSupport.Ct);
        var requestsBeforeWait = handler.Requests.Count;
        time.Advance(TimeSpan.FromSeconds(RetrySeconds));
        var completion = await pending;

        Assert.Equal(1, requestsBeforeWait);
        Assert.Equal(TimeSpan.FromSeconds(RetrySeconds), time.DueTimes.Single());
        Assert.Equal(Primary, completion.Model);
        Assert.Equal(2, Requests(handler, Primary));
        Assert.Equal(0, Requests(handler, Backup));
    }

    [Fact]
    public async Task CompleteAsync_PerMinuteLimitStillFailsAfterWait_WaitsOnlyOncePerModelThenFallsBack()
    {
        var time = new SignalingTimeProvider();
        var handler = Endpoint(call => call.Model == Primary ? Quota(PerMinuteMessage) : Ok(call.Model));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { Time = time });

        var pending = provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);
        await time.TimerCreated.WaitAsync(TestSupport.Ct);
        time.Advance(TimeSpan.FromSeconds(RetrySeconds));
        var completion = await pending;

        Assert.Equal(Backup, completion.Model);
        Assert.Equal(2, Requests(handler, Primary));
        Assert.Single(time.DueTimes);
    }

    [Theory]
    [InlineData("Quota PerDay exceeded. Please retry in 5s.")]
    [InlineData("upstream said no")]
    [InlineData("Quota exceeded. Please retry in 120s.")]
    public async Task CompleteAsync_QuotaWithoutUsableDelay_FallsBackImmediatelyWithoutWaiting(string message)
    {
        var time = new SignalingTimeProvider();
        var handler = Endpoint(call => call.Model == Primary ? Quota(message) : Ok(call.Model));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { Time = time });

        var completion = await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(Backup, completion.Model);
        Assert.Empty(time.DueTimes);
        Assert.Equal(1, Requests(handler, Primary));
    }

    [Fact]
    public async Task CompleteAsync_CancelledDuringQuotaWait_ThrowsOperationCanceled()
    {
        var time = new SignalingTimeProvider();
        var handler = Endpoint(call => call.Model == Primary ? Quota(PerMinuteMessage) : Ok(call.Model));
        var provider = Provider(handler, Options(Backup), new GeminiWireSetup { Time = time });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);

        var pending = provider.CompleteAsync(GeminiWire.Request, cts.Token);
        await time.TimerCreated.WaitAsync(TestSupport.Ct);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, Requests(handler, Primary));
        Assert.Equal(0, Requests(handler, Backup));
    }

    private static string PerMinuteMessage => $"GenerateRequestsPerMinutePerProjectPerModel exceeded. Please retry in {RetrySeconds}s.";

    private sealed class RecordingBudget(Func<string, int> count) : IGeminiInputBudget
    {
        public List<string> Models { get; } = [];

        public int MaxInputTokens => TokenLimit;

        public Task<int> CountAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Models.Add(request.Model!);
            return Task.FromResult(count(request.Model!));
        }
    }

    private sealed class SignalingTimeProvider : FakeTimeProvider
    {
        private readonly TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<TimeSpan> _dueTimes = [];

        public Task TimerCreated => _timerCreated.Task;

        public IReadOnlyList<TimeSpan> DueTimes
        {
            get
            {
                lock (_dueTimes)
                {
                    return [.. _dueTimes];
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            lock (_dueTimes)
            {
                _dueTimes.Add(dueTime);
            }

            _timerCreated.TrySetResult();
            return timer;
        }
    }
}
