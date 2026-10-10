using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Remote.Selection;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Secrets;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

internal sealed class ScriptedRemoteClient(SourceType provider) : IRemoteRepositoryClient
{
    public SourceType Provider => provider;

    public IReadOnlyList<RemoteRepository> Repositories { get; set; } = [];

    public IReadOnlyList<RemoteBranch> Branches { get; set; } = [];

    public Exception? ListError { get; set; }

    public Exception? BranchError { get; set; }

    public string? LastScope { get; private set; }

    public int ListCalls { get; private set; }

    public TaskCompletionSource? Gate { get; set; }

    public async Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(string? scope, CancellationToken cancellationToken)
    {
        ListCalls++;
        LastScope = scope;
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        return ListError is null ? Repositories : throw ListError;
    }

    public Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(RemoteRepository repository, CancellationToken cancellationToken) =>
        BranchError is null ? Task.FromResult(Branches) : Task.FromException<IReadOnlyList<RemoteBranch>>(BranchError);

    public RemoteTree Tree { get; set; } = new("3f2a9c1e55", [], false);

    public int TreeCalls { get; private set; }

    public Exception? TreeError { get; set; }

    public TaskCompletionSource? TreeGate { get; set; }

    public async Task<RemoteTree> GetTreeAsync(RemoteRepository repository, string branch, CancellationToken cancellationToken)
    {
        TreeCalls++;
        if (TreeError is not null)
        {
            throw TreeError;
        }

        if (TreeGate is not null)
        {
            await TreeGate.Task.WaitAsync(cancellationToken);
        }

        return Tree;
    }

    public Task<RemoteBlob> OpenBlobAsync(RemoteRepository repository, string blobSha, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeRemoteFetcher : IRemoteFetcher
{
    public RemoteFetchResult Result { get; set; } = new(SourceType.Github, "3f2a9c1e55", [new FetchedFile("a.md", "docs/a.md"), new FetchedFile("b.md", "docs/b.md")], 2, 0, 1, 0, false);

    public Exception? Error { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public RemoteFetchRequest? LastRequest { get; private set; }

    public int Calls { get; private set; }

    public async Task<RemoteFetchResult> FetchAsync(
        RemoteFetchRequest request,
        IProgress<RemoteFetchProgress> progress,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        progress.Report(new RemoteFetchProgress(RemoteFetchPhase.Downloading, 1, 2));
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        return Error is null ? Result : throw Error;
    }
}

internal sealed class FakeRateLimitMonitor : IRateLimitMonitor
{
    public event EventHandler<RateLimitStatus>? StatusChanged;

    public RateLimitStatus Current { get; set; } = RateLimitStatus.Running(SourceType.Github);

    public RateLimitStatus GetStatus(SourceType provider) => Current;

    public void Raise(RateLimitStatus status) => StatusChanged?.Invoke(this, status);
}

internal sealed class FakeLinkLauncher : IExternalLinkLauncher
{
    public List<Uri> Opened { get; } = [];

    public bool TryOpen(Uri uri)
    {
        Opened.Add(uri);
        return true;
    }
}

internal sealed class FakeSshCloser : ISshConnectionCloser
{
    public int Closed { get; private set; }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        Closed++;
        return Task.CompletedTask;
    }
}

internal sealed class RemoteSourceHarness
{
    public const string GitHubUrl = "https://github.com/octo";
    public const string AzureUrl = "https://dev.azure.com/contoso";

    private IntakeProgressViewModel? _intake;

    public RemoteSourceHarness(Action<RemoteFetchOptions>? configureOptions = null)
    {
        configureOptions?.Invoke(FetchOptions.Value);
        Settings = new SettingsService(Store, Secrets, new FakeGeminiKeyStore());
        var clients = new FakeRemoteClients(GitHub, AzureDevOps, Cortexa);
        var dependencies = new RemoteSourceDependencies(
            clients,
            Fetcher,
            RateLimits,
            Settings,
            Credentials,
            SshCloser,
            Links,
            Picker,
            FetchOptions,
            Intake,
            new FileTreeBuilder(new RemoteFileFilter(FetchOptions)));
        ViewModel = new RemoteSourceViewModel(
            dependencies,
            TimeProvider.System,
            NullLogger<RemoteSourceViewModel>.Instance);
    }

    public IOptions<RemoteFetchOptions> FetchOptions { get; } = Options.Create(new RemoteFetchOptions());

    public FakeTimeProvider IntakeTime { get; } = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));

    public IntakeProgressViewModel Intake => _intake ??= new IntakeProgressViewModel(IntakeTime);

    public FakeUserSettingsStore Store { get; } = new();

    public InMemorySecretStore Secrets { get; } = new();

    public InMemorySessionCredentials Credentials { get; } = new();

    public FakeSshCloser SshCloser { get; } = new();

    public SettingsService Settings { get; }

    public ScriptedRemoteClient GitHub { get; } = new(SourceType.Github);

    public ScriptedRemoteClient AzureDevOps { get; } = new(SourceType.AzureDevops);

    public ScriptedRemoteClient Cortexa { get; } = new(SourceType.CortexaRepo);

    public FakeRemoteFetcher Fetcher { get; } = new();

    public FakeRateLimitMonitor RateLimits { get; } = new();

    public FakeLinkLauncher Links { get; } = new();

    public FakeFilePicker Picker { get; } = new([]);

    public FakeNavigationService Navigation { get; } = new();

    public RemoteSourceViewModel ViewModel { get; }

    public static RemoteRepository GitHubRepo(string name, long sizeBytes = 1024, bool isPrivate = false) =>
        new(SourceType.Github, "octo", null, name, $"octo/{name}", "main", $"https://github.com/octo/{name}", sizeBytes, isPrivate);

    public static RemoteRepository AzureRepo(string name, string project = "Research", DateTimeOffset? updatedAt = null) =>
        new(SourceType.AzureDevops, "contoso", project, name, $"contoso/{project}/{name}", "main", "https://dev.azure.com/contoso", 2048, true, null, updatedAt);

    public async Task<RemoteSourceViewModel> OpenAzureAsync(params RemoteRepository[] repositories)
    {
        AzureDevOps.Repositories = repositories;
        return await ConnectAsync(SourceType.AzureDevops, AzureUrl);
    }

    public static RemoteRepository CortexaRepo(string name, string? tag = "github", string owner = "octo", string branch = "saved") =>
        new(SourceType.CortexaRepo, owner, tag, name, $"{owner}/{name}", branch, $"cortexa://{tag}/{owner}/{name}", 1024, false);

    public async Task<RemoteSourceViewModel> OpenCortexaAsync(params RemoteRepository[] repositories)
    {
        Cortexa.Repositories = repositories;
        ViewModel.SelectedSource = SourceType.CortexaRepo;
        await Task.Yield();
        return ViewModel;
    }

    public async Task<RemoteSourceViewModel> OpenGitHubAsync(params RemoteRepository[] repositories)
    {
        GitHub.Repositories = repositories;
        return await ConnectAsync(SourceType.Github, GitHubUrl);
    }

    public async Task<RemoteSourceViewModel> ConnectAsync(SourceType source, string url, string token = "token")
    {
        ViewModel.SelectedSource = source;
        ViewModel.OrgUrl = url;
        ViewModel.Token = token;
        await ViewModel.ConnectCommand.ExecuteAsync(null);
        return ViewModel;
    }
}
