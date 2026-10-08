using Collector.Application.Auth;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Upload;
using Collector.Presentation.Navigation;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;

namespace Collector.Tests.Presentation;

internal sealed class FakeFilePicker(IReadOnlyList<string> paths) : IFilePicker
{
    public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken) => Task.FromResult(paths);
}

internal sealed class FakeKnowledgeRunner : IKnowledgeRunner
{
    public KnowledgeRunOutcome Outcome { get; set; } = new(KnowledgeRunStatus.Completed, ReviewData.Run([ReviewData.Item("Default idea")]));

    public TaskCompletionSource<KnowledgeRunOutcome>? Gate { get; set; }

    public IReadOnlyList<string> LastDocumentIds { get; private set; } = [];

    public KnowledgeRunRequest? LastRequest { get; private set; }

    public IProgress<ExtractionProgress>? Progress { get; private set; }

    public int Calls { get; private set; }

    public Task<KnowledgeRunOutcome> RunAsync(
        KnowledgeRunRequest request,
        IProgress<ExtractionProgress> progress,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        LastDocumentIds = request.DocumentIds;
        Progress = progress;
        var gate = Gate;
        if (gate is null)
        {
            return Task.FromResult(Outcome);
        }

        cancellationToken.Register(() => gate.TrySetResult(new KnowledgeRunOutcome(KnowledgeRunStatus.Canceled)));
        return gate.Task;
    }
}

internal sealed class FakeNavigationService : INavigationService
{
    public IReadOnlyList<ScreenRegistration> Screens => [];

    public ScreenRegistration? CurrentScreen { get; set; }

    public object? CurrentViewModel => null;

    public List<string> Visited { get; } = [];

    public event EventHandler? Navigated
    {
        add { }
        remove { }
    }

    public static ScreenRegistration Screen(string key) => new()
    {
        Key = key,
        Title = key,
        ViewModelType = typeof(object),
        RequiresSignIn = false,
        Glyph = string.Empty,
        Placement = NavPlacement.Main,
        Order = 0,
    };

    public void NavigateTo(string key) => Visited.Add(key);
}

internal sealed class FakeClipboard : IClipboard
{
    public bool TrySetText(string text) => true;
}

internal sealed class FixedAppVersion : IAppVersion
{
    public string Current => "1.0.0";
}

internal sealed class GateableUploadClient(FakeUploadClient inner) : IKnowledgeUploadClient
{
    public TaskCompletionSource? Gate { get; set; }

    public async Task<KnowledgeUploadResult> UploadAsync(KnowledgeUploadRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (Gate is not null)
        {
            await Gate.Task;
        }

        return await inner.UploadAsync(request, idempotencyKey, cancellationToken);
    }
}

internal sealed class KnowledgeHarness
{
    public KnowledgeHarness(SessionState session = SessionState.SignedIn)
    {
        Session = new FakeSession { Current = session };
        ViewModel = new KnowledgeRunViewModel(Runner, State, Navigation, Session);
        Navigation.CurrentScreen = FakeNavigationService.Screen(ScreenKeys.Extract);
    }

    public FakeKnowledgeRunner Runner { get; } = new();

    public FakeNavigationService Navigation { get; } = new();

    public FakeSession Session { get; }

    public KnowledgeRunState State { get; } = new();

    public KnowledgeRunViewModel ViewModel { get; }
}
