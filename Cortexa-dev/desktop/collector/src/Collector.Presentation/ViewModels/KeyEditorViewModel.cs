using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class KeyEditorViewModel : ObservableObject, IPasswordHost
{
    private readonly Func<KeyEditorViewModel, CancellationToken, Task> _save;
    private readonly Action _cancel;

    public KeyEditorViewModel(
        KeyEditorLabels labels,
        Func<KeyEditorViewModel, CancellationToken, Task> save,
        Action cancel)
    {
        Labels = labels;
        _save = save;
        _cancel = cancel;
    }

    public KeyEditorLabels Labels { get; }

    public IPasswordSource? PasswordSource { get; set; }

    public bool IsEditable => !IsSaving;

    public string SaveLabel => IsSaving ? Resources.SettingsStrings.Saving : Resources.SettingsStrings.SaveKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(SaveLabel))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string TakeKey() => PasswordSource?.GetPassword().Trim() ?? string.Empty;

    public void ClearPassword() => PasswordSource?.Clear();

    public void OnPasswordEdited() => Error = null;

    [RelayCommand]
    private Task SaveAsync(CancellationToken cancellationToken) => _save(this, cancellationToken);

    [RelayCommand]
    private void Cancel() => _cancel();
}

public sealed record KeyEditorLabels(string Label, string AutomationName, string SaveName, string CancelName);
