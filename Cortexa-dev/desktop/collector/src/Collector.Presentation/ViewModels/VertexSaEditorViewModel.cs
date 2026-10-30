using System.IO;
using Collector.Application.Ports;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class VertexSaEditorViewModel : ObservableObject
{
    private readonly IFilePicker _filePicker;
    private readonly Func<VertexSaEditorViewModel, CancellationToken, Task> _save;
    private readonly Action _cancel;

    public VertexSaEditorViewModel(
        KeyEditorLabels labels,
        IFilePicker filePicker,
        Func<VertexSaEditorViewModel, CancellationToken, Task> save,
        Action cancel)
    {
        Labels = labels;
        _filePicker = filePicker;
        _save = save;
        _cancel = cancel;
    }

    public KeyEditorLabels Labels { get; }

    public bool IsEditable => !IsSaving;

    public string SaveLabel => IsSaving ? SettingsStrings.Saving : Labels.SaveText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(SaveLabel))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string PasteText { get; set; } = string.Empty;

    public string TakeJson() => PasteText.Trim();

    public void ClearPaste() => PasteText = string.Empty;

    partial void OnPasteTextChanged(string value) => Error = null;

    [RelayCommand]
    private Task SaveAsync(CancellationToken cancellationToken) => _save(this, cancellationToken);

    [RelayCommand]
    private void Cancel() => _cancel();

    [RelayCommand]
    private async Task BrowseAsync(CancellationToken cancellationToken)
    {
        var path = await _filePicker.PickSingleFileAsync(
            SettingsStrings.VertexBrowseName,
            "JSON files (*.json)|*.json|All files (*.*)|*.*",
            cancellationToken);
        if (path is null)
        {
            return;
        }

        try
        {
            PasteText = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = SettingsStrings.VertexServiceAccountInvalid;
        }
    }
}
