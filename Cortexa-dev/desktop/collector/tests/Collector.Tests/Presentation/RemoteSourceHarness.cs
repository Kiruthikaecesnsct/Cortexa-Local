using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Navigation;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    public Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(string? scope, CancellationToken cancellationToken)
    {
        ListCalls++;
        LastScope = scope;
        return ListError is null ? Task.FromResult(Repositories) : Task.FromException<IReadOnlyList<RemoteRepository>>(ListError);
    }

    public Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(RemoteRepository repository, CancellationToken cancellationToken) =>
        BranchError is null ? Task.FromResult(Branches) : Task.FromException<IReadOnlyList<RemoteBranch>>(BranchError);

    public Task<RemoteTree> GetTreeAsync(RemoteRepository repository, string branch, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RemoteBlob> OpenBlobAsync(RemoteRepository repository, string blobSha, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeRemoteFetcher : IRemoteFetcher
{
    public RemoteFetchResult Result { get; set; } = new(SourceType.Github, "3f2a9c1e55", ["a.md", "b.md"], 2, 0, 1, 0, false);

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

internal sealed class RemoteSourceHarness
{
    public RemoteSourceHarness()
    {
        Settings = new SettingsService(Store, Secrets);
        var clients = new FakeRemoteClients(GitHub, AzureDevOps);
        var dependencies = new RemoteSourceDependencies(
            clients,
            Fetcher,
            RateLimits,
            Settings,
            Links,
            Options.Create(new RemoteFetchOptions()));
        ViewModel = new RemoteSourceViewModel(
            dependencies,
            new SettingsShortcut(Navigation),
            TimeProvider.System,
            NullLogger<RemoteSourceViewModel>.Instance);
    }

    public FakeUserSettingsStore Store { get; } = new();

    public InMemorySecretStore Secrets { get; } = new();

    public SettingsService Settings { get; }

    public ScriptedRemoteClient GitHub { get; } = new(SourceType.Github);

    public ScriptedRemoteClient AzureDevOps { get; } = new(SourceType.AzureDevops);

    public FakeRemoteFetcher Fetcher { get; } = new();

    public FakeRateLimitMonitor RateLimits { get; } = new();

    public FakeLinkLauncher Links { get; } = new();

    public FakeNavigationService Navigation { get; } = new();

    public RemoteSourceViewModel ViewModel { get; }

    public void AddToken(SecretSlot slot) => Secrets.Values[slot] = "token";

    public static RemoteRepository GitHubRepo(string name, long sizeBytes = 1024, bool isPrivate = false) =>
        new(SourceType.Github, "octo", null, name, $"octo/{name}", "main", $"https://github.com/octo/{name}", sizeBytes, isPrivate);

    public static RemoteRepository AzureRepo(string name) =>
        new(SourceType.AzureDevops, "contoso", "Research", name, $"contoso/Research/{name}", "main", "https://dev.azure.com/contoso", 2048, true);

    public async Task<RemoteSourceViewModel> OpenGitHubAsync(params RemoteRepository[] repositories)
    {
        AddToken(SecretSlot.GitHubPat);
        GitHub.Repositories = repositories;
        ViewModel.SelectedSource = SourceType.Github;
        await Task.Yield();
        return ViewModel;
    }
}
