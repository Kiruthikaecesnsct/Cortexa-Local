using System.IO;
using Collector.Application.Auth;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Application.Upload;
using Collector.Domain.Enums;
using Collector.Domain.Upload;
using Collector.Presentation.Navigation;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Presentation;

internal sealed class FakeFilePicker(IReadOnlyList<string> paths) : IFilePicker
{
    public string? SingleFilePath { get; set; }

    public string? SaveFilePath { get; set; }

    public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken) => Task.FromResult(paths);

    public Task<string?> PickSingleFileAsync(string dialogTitle, string filter, CancellationToken cancellationToken) =>
        Task.FromResult(SingleFilePath);

    public Task<string?> PickSaveFileAsync(string dialogTitle, string filter, string defaultFileName, CancellationToken cancellationToken) =>
        Task.FromResult(SaveFilePath);
}

internal sealed class FakeKnowledgePdfExporter : IKnowledgePdfExporter
{
    public int ExportCalls { get; private set; }

    public KnowledgePdfReport? LastReport { get; private set; }

    public string? LastPath { get; private set; }

    public bool ThrowOnExport { get; set; }

    public Task ExportAsync(KnowledgePdfReport report, string outputPath, CancellationToken cancellationToken)
    {
        if (ThrowOnExport)
        {
            throw new IOException("Simulated export failure.");
        }

        ExportCalls++;
        LastReport = report;
        LastPath = outputPath;
        return Task.CompletedTask;
    }
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

    public async Task<KnowledgeUploadResult> UploadAsync(UploadPayload payload, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (Gate is not null)
        {
            await Gate.Task;
        }

        return await inner.UploadAsync(payload, idempotencyKey, cancellationToken);
    }
}

internal sealed class KnowledgeHarness
{
    public KnowledgeHarness(SessionState session = SessionState.SignedIn)
    {
        Session = new FakeSession { Current = session };
        Navigator = new SettingsNavigator(Navigation);
        ViewModel = new KnowledgeRunViewModel(Runner, State, new KnowledgeRunNavigation(Navigation, Navigator), Session)
        {
            Readiness = ProviderReadinessState.Ready,
        };
        Navigation.CurrentScreen = FakeNavigationService.Screen(ScreenKeys.Extract);
    }

    public SettingsNavigator Navigator { get; }

    public FakeKnowledgeRunner Runner { get; } = new();

    public FakeNavigationService Navigation { get; } = new();

    public FakeSession Session { get; }

    public KnowledgeRunState State { get; } = new();

    public KnowledgeRunViewModel ViewModel { get; }
}

internal sealed class FakeModelCatalog : IProviderModelCatalog
{
    public Dictionary<CollectorProvider, string[]> Models { get; } = new()
    {
        [CollectorProvider.Claude] = ["claude-a", "claude-b"],
        [CollectorProvider.Gemini] = ["gemini-a", "gemini-b"],
        [CollectorProvider.Bedrock] = ["bedrock-a"],
    };

    public IReadOnlyList<string> ModelsFor(CollectorProvider provider) => Models.GetValueOrDefault(provider) ?? [];

    public string? DefaultModelFor(CollectorProvider provider) => ModelsFor(provider).FirstOrDefault();
}

internal sealed class FakeProviderReadiness : IProviderReadiness
{
    public Dictionary<CollectorProvider, ProviderReadinessState> States { get; } = new()
    {
        [CollectorProvider.Claude] = ProviderReadinessState.Ready,
        [CollectorProvider.Gemini] = ProviderReadinessState.Ready,
        [CollectorProvider.Bedrock] = ProviderReadinessState.Ready,
    };

    public TaskCompletionSource? Gate { get; set; }

    public int Checks { get; private set; }

    public event EventHandler? Changed;

    public async Task<ProviderReadinessState> CheckAsync(CollectorProvider provider, CancellationToken cancellationToken)
    {
        Checks++;
        if (Gate is not null)
        {
            await Gate.Task;
        }

        return States[provider];
    }

    public async Task<bool> IsReadyAsync(CollectorProvider provider, CancellationToken cancellationToken) =>
        await CheckAsync(provider, cancellationToken) == ProviderReadinessState.Ready;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class ModelChoiceHarness
{
    public ModelChoiceHarness(CollectorProvider configured = CollectorProvider.Claude)
    {
        Choices = new AiModelChoiceService(
            Store,
            Catalog,
            MsOptions.Create(new KnowledgeExtractionOptions { Provider = configured }));
        Navigator = new SettingsNavigator(Navigation);
    }

    public FakeUserSettingsStore Store { get; } = new();

    public FakeModelCatalog Catalog { get; } = new();

    public FakeProviderReadiness Readiness { get; } = new();

    public FakeNavigationService Navigation { get; } = new();

    public AiModelChoiceService Choices { get; }

    public SettingsNavigator Navigator { get; }

    public AiModelSectionViewModel CreateSection() =>
        new(Choices, Readiness, Navigator, NullLogger<AiModelSectionViewModel>.Instance);

    public ActiveModelViewModel CreateActiveModel() => new(Choices, Readiness, Navigator);
}

internal sealed class StubBedrockSso : IBedrockSsoCredentials
{
    public BedrockSsoStatus Status { get; set; } = BedrockSsoStatus.NotConnected;

    public Exception? StatusFailure { get; set; }

    public Task<BedrockSsoStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        StatusFailure is null ? Task.FromResult(Status) : Task.FromException<BedrockSsoStatus>(StatusFailure);

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
