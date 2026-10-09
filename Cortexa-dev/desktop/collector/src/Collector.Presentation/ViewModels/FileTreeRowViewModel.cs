using Collector.Application.Remote;
using Collector.Application.Remote.Selection;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed class FileTreeRowViewModel : ObservableObject
{
    private bool _isExpanded;

    public FileTreeRowViewModel(FileTreeNode node, int depth, Action<FileTreeRowViewModel> toggleExpand, Action<FileTreeRowViewModel> toggleCheck)
    {
        Node = node;
        Depth = depth;
        ToggleExpandCommand = new RelayCommand(() => toggleExpand(this));
        ToggleCheckCommand = new RelayCommand(() => toggleCheck(this));
    }

    public FileTreeNode Node { get; }

    public int Depth { get; }

    public string Name => Node.Name;

    public bool IsFolder => Node.IsFolder;

    public bool? IsChecked => Node.IsChecked;

    public bool IsMuted => Node.Verdict is RemoteEntryVerdict.Unsupported or RemoteEntryVerdict.TooLarge;

    public IRelayCommand ToggleExpandCommand { get; }

    public IRelayCommand ToggleCheckCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string SizeText => Node.IsFolder
        ? RemoteSourceStrings.FolderSize(Node.Selected.Files, Node.Total.Files)
        : RemoteSizeFormatter.Format(Node.SizeBytes ?? 0);

    public string VerdictLabel => Node.Verdict switch
    {
        RemoteEntryVerdict.Unsupported => RemoteSourceStrings.UnsupportedLabel,
        RemoteEntryVerdict.TooLarge => RemoteSourceStrings.TooLargeLabel,
        _ => string.Empty,
    };

    public bool HasVerdict => VerdictLabel.Length > 0;

    public bool IsTooLarge => Node.Verdict == RemoteEntryVerdict.TooLarge;

    public string AutomationName => Node.IsFolder ? FolderName() : FileName();

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(AutomationName));
    }

    private string FolderName() => RemoteSourceStrings.FolderRowName(
        Name,
        Depth + 1,
        IsExpanded,
        RemoteSourceStrings.CheckStateName(IsChecked),
        RemoteSourceStrings.FolderSize(Node.Selected.Files, Node.Total.Files));

    private string FileName() => RemoteSourceStrings.FileRowName(
        Name,
        SizeText,
        Depth + 1,
        IsChecked == true,
        RemoteSourceStrings.FileVerdictSuffix(Node.Verdict == RemoteEntryVerdict.Unsupported, IsTooLarge));
}
