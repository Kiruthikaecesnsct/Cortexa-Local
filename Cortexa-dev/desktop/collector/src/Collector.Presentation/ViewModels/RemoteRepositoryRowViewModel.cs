using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed class RemoteRepositoryRowViewModel
{
    public RemoteRepositoryRowViewModel(RemoteRepository repository, long limitBytes)
    {
        Repository = repository;
        IsTooBig = repository.SizeBytes > limitBytes;
        SizeText = RemoteSizeFormatter.Format(repository.SizeBytes);
        LimitText = RemoteSizeFormatter.Format(limitBytes);
        AutomationName = RemoteSourceStrings.RepositoryAutomationName(
            repository.Name,
            repository.IsPrivate,
            repository.DefaultBranch,
            SizeText,
            IsTooBig ? LimitText : null);
    }

    public RemoteRepository Repository { get; }

    public string Name => Repository.Name;

    public string FullName => Repository.FullName;

    public string DefaultBranch => Repository.DefaultBranch;

    public bool IsPrivate => Repository.IsPrivate;

    public bool IsTooBig { get; }

    public string SizeText { get; }

    public string LimitText { get; }

    public string OverLimitTag => RemoteSourceStrings.OverLimitTag(LimitText);

    public string AutomationName { get; }

    public string HelpText => FullName;
}

public sealed class BranchOptionViewModel(string name, string? commitSha, bool isDefault)
{
    private const int ShortShaLength = 7;

    public string Name { get; } = name;

    public string DisplayName { get; } = isDefault ? name + RemoteSourceStrings.DefaultSuffix : name;

    public string? CommitSha { get; } = commitSha;

    public string? ShortSha => CommitSha is { Length: > 0 } sha ? sha[..Math.Min(ShortShaLength, sha.Length)] : null;
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
