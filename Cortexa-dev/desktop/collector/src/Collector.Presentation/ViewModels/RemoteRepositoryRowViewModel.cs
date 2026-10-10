using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed class RemoteRepositoryRowViewModel
{
    private const string GitHubUpstream = "github";
    private const string AzureUpstream = "azure-devops";

    private readonly string? _relativeUpdated;

    public RemoteRepositoryRowViewModel(RemoteRepository repository, long limitBytes, TimeProvider? time = null)
    {
        Repository = repository;
        IsTooBig = repository.SizeBytes > limitBytes;
        SizeText = RemoteSizeFormatter.Format(repository.SizeBytes);
        LimitText = RemoteSizeFormatter.Format(limitBytes);
        UpstreamTag = ResolveUpstreamTag(repository);
        _relativeUpdated = repository.UpdatedAt is { } updated
            ? RelativeTimeFormatter.Describe((time ?? TimeProvider.System).GetUtcNow() - updated)
            : null;
        UpdatedText = FormatUpdated(_relativeUpdated, repository.Provider);
        AutomationName = repository.Provider switch
        {
            SourceType.CortexaRepo => CortexaAutomationName(),
            SourceType.AzureDevops => AzureAutomationName(),
            _ => RemoteSourceStrings.RepositoryAutomationName(
                repository.Name,
                repository.IsPrivate,
                repository.DefaultBranch,
                SizeText,
                IsTooBig ? LimitText : null),
        };
    }

    public RemoteRepository Repository { get; }

    public string Name => Repository.Name;

    public string FullName => Repository.FullName;

    public string DefaultBranch => Repository.DefaultBranch;

    public bool IsPrivate => Repository.IsPrivate;

    public string VisibilityText => IsPrivate ? RemoteSourceStrings.PrivateTag : RemoteSourceStrings.PublicTag;

    public bool IsAzure => Repository.Provider == SourceType.AzureDevops;

    public string? Description => Repository.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Repository.Description);

    public string UpdatedText { get; }

    public bool HasUpdatedText => UpdatedText.Length > 0;

    public bool ShowUpdatedBeforeSize => HasUpdatedText && !IsAzure;

    public bool ShowUpdatedAfterSize => HasUpdatedText && IsAzure;

    public string? UpstreamTag { get; }

    public bool HasUpstreamTag => UpstreamTag is not null;

    public bool IsTooBig { get; }

    public string SizeText { get; }

    public string LimitText { get; }

    public string OverLimitTag => RemoteSourceStrings.OverLimitTag(LimitText);

    public string AutomationName { get; }

    public string HelpText => FullName;

    private static string? ResolveUpstreamTag(RemoteRepository repository) =>
        repository.Provider != SourceType.CortexaRepo
            ? null
            : repository.Project switch
            {
                GitHubUpstream => RemoteSourceStrings.UpstreamGitHubTag,
                AzureUpstream => RemoteSourceStrings.UpstreamAzureTag,
                _ => null,
            };

    private static string FormatUpdated(string? relative, SourceType provider)
    {
        if (relative is null)
        {
            return string.Empty;
        }

        return provider == SourceType.AzureDevops
            ? RemoteSourceStrings.ProjectUpdatedText(relative)
            : RemoteSourceStrings.UpdatedText(relative);
    }

    private string AzureAutomationName() => RemoteSourceStrings.AzureRepositoryAutomationName(
        new AzureRowDescription(
            Name,
            Repository.Project ?? string.Empty,
            DefaultBranch,
            SizeText,
            _relativeUpdated,
            IsTooBig ? LimitText : null));

    private string CortexaAutomationName() => RemoteSourceStrings.CortexaRepositoryAutomationName(
        new CortexaRowDescription(FullName, DefaultBranch, UpstreamTag, SizeText, IsTooBig ? LimitText : null));
}

public sealed class BranchOptionViewModel(string name, string? commitSha, bool isDefault, bool isProtected = false)
{
    public string Name { get; } = name;

    public string DisplayName { get; } = isDefault ? name + RemoteSourceStrings.DefaultSuffix : name;

    public string? CommitSha { get; } = commitSha;

    public bool IsDefault { get; } = isDefault;

    public bool IsProtected { get; } = isProtected;

    public string? ShortSha => BranchOrdering.ShortSha(CommitSha);

    public string AutomationName => RemoteSourceStrings.BranchAutomationName(Name, IsDefault, IsProtected, ShortSha);
}

public sealed class SourceChipViewModel : ObservableObject
{
    private readonly Action<SourceType> _select;
    private bool _isSelected;

    public SourceChipViewModel(SourceType source, string accessLabel, string automationName, Action<SourceType> select)
    {
        Source = source;
        AccessLabel = accessLabel;
        AutomationName = automationName;
        _select = select;
    }

    public SourceType Source { get; }

    public string AccessLabel { get; }

    public string AutomationName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
            {
                _select(Source);
            }
        }
    }

    public void Sync(SourceType selected) => SetProperty(ref _isSelected, selected == Source, nameof(IsSelected));
}
