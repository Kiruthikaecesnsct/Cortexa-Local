using System.IO;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public static class VertexRowFocusKeys
{
    public const string Primary = "Primary";
    public const string Clear = "Clear";
    public const string Keep = "Keep";
    public const string ConfirmClear = "ConfirmClear";
    public const string Editor = "Editor";
    public const string ProjectId = "ProjectId";
    public const string Location = "Location";
}

public sealed partial class VertexRowViewModel : FocusableViewModel
{
    private const string ProviderWord = "Vertex AI";
    private const string NounWord = "service account";

    private readonly IVertexCredentialStore _credentials;
    private readonly ISecretStore _secrets;
    private readonly IUserSettingsStore _store;
    private readonly IVertexSettingsState _state;
    private readonly IFilePicker _filePicker;
    private readonly ILogger _logger;
    private string _savedProjectId;
    private string _savedLocation;
    private bool _projectIdLive;
    private bool _locationLive;

    public VertexRowViewModel(
        IVertexCredentialStore credentials,
        ISecretStore secrets,
        IUserSettingsStore store,
        IVertexSettingsState state,
        IFilePicker filePicker,
        ILogger logger)
    {
        _credentials = credentials;
        _secrets = secrets;
        _store = store;
        _state = state;
        _filePicker = filePicker;
        _logger = logger;

        var saved = store.GetVertexUserSettings();
        _savedProjectId = saved.ProjectId;
        _savedLocation = saved.Location;
        ProjectId = saved.ProjectId;
        Location = saved.Location;
    }

    public event EventHandler? EditorStateChanged;

    public event EventHandler? StatusChanged;

    public string Name => SettingsStrings.VertexName;

    public string Description => SettingsStrings.VertexDescription;

    public bool IsChecking => ChipKind == VertexSaChipKind.Checking;

    public bool HasSa => ChipKind == VertexSaChipKind.Saved;

    public bool ShowActions => !IsChecking && !IsConfirmingClear;

    public bool ShowStatusHelper => ChipKind == VertexSaChipKind.Unknown;

    public string StatusHelper => SettingsStrings.VertexStatusUnknownHelper;

    public string ChipText => ChipKind switch
    {
        VertexSaChipKind.Saved => SettingsStrings.VertexSaChipSaved,
        VertexSaChipKind.NotSaved => SettingsStrings.VertexSaChipNotSaved,
        VertexSaChipKind.Unknown => SettingsStrings.ChipUnknown,
        _ => SettingsStrings.ChipChecking,
    };

    public string ChipAutomationName => SettingsStrings.ChipName(ProviderWord, NounWord, ChipText.TrimEnd('…'));

    public string ActionLabel => HasSa ? SettingsStrings.Replace : SettingsStrings.AddServiceAccount;

    public string ActionAutomationName =>
        HasSa ? SettingsStrings.ReplaceName(ProviderWord, NounWord) : SettingsStrings.AddName(ProviderWord, NounWord);

    public string ClearAutomationName => SettingsStrings.ClearName(ProviderWord, NounWord);

    public string ConfirmText => SettingsStrings.ConfirmClear(ProviderWord, SettingsStrings.VertexRunsName, NounWord);

    public string ConfirmAutomationName => SettingsStrings.ConfirmClearName(ProviderWord, NounWord);

    public string KeepAutomationName => SettingsStrings.KeepName(ProviderWord, NounWord);

    public string ConfirmLabel => IsClearing ? SettingsStrings.Clearing : SettingsStrings.KeepServiceAccount;

    public bool IsConfirmEnabled => !IsClearing;

    public bool HasEditor => Editor is not null;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool AreFieldsEnabled => !IsSaving;

    public string SaveLabel => IsSaving ? SettingsStrings.Saving : SettingsStrings.SaveVertexSettings;

    public bool IsDirty => Normalize(ProjectId) != _savedProjectId || Normalize(Location) != _savedLocation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsChecking),
        nameof(HasSa),
        nameof(ShowActions),
        nameof(ShowStatusHelper),
        nameof(ChipText),
        nameof(ChipAutomationName),
        nameof(ActionLabel),
        nameof(ActionAutomationName))]
    public partial VertexSaChipKind ChipKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    public partial VertexSaEditorViewModel? Editor { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool IsConfirmingClear { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel), nameof(IsConfirmEnabled))]
    public partial bool IsClearing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string ProjectId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Location { get; set; }

    [ObservableProperty]
    public partial string? ProjectIdError { get; set; }

    [ObservableProperty]
    public partial string? LocationError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreFieldsEnabled), nameof(SaveLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? SaveBanner { get; set; }

    partial void OnProjectIdChanged(string value)
    {
        SaveBanner = null;
        if (_projectIdLive)
        {
            ProjectIdError = string.IsNullOrWhiteSpace(value) ? SettingsStrings.VertexProjectIdEmpty : null;
        }
    }

    partial void OnLocationChanged(string value)
    {
        SaveBanner = null;
        if (_locationLive)
        {
            LocationError = string.IsNullOrWhiteSpace(value) ? SettingsStrings.VertexLocationEmpty : null;
        }
    }

    public async Task LoadStatusAsync(CancellationToken cancellationToken)
    {
        if (ChipKind is VertexSaChipKind.Saved or VertexSaChipKind.NotSaved)
        {
            return;
        }

        ChipKind = VertexSaChipKind.Checking;
        try
        {
            var serviceAccount = await _credentials.GetServiceAccountJsonAsync(cancellationToken);
            ChipKind = string.IsNullOrWhiteSpace(serviceAccount) ? VertexSaChipKind.NotSaved : VertexSaChipKind.Saved;
        }
        catch (Exception ex) when (ex is SecretStoreException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the Vertex AI service account status.");
            ChipKind = VertexSaChipKind.Unknown;
        }
    }

    public void CloseEditor(bool returnFocus)
    {
        if (Editor is null)
        {
            return;
        }

        Editor.ClearPaste();
        Editor = null;
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
        if (returnFocus)
        {
            RequestFocus(VertexRowFocusKeys.Primary);
        }
    }

    [RelayCommand]
    private void OpenEditor()
    {
        Message = null;
        ErrorMessage = null;
        Editor = new VertexSaEditorViewModel(EditorLabels(), _filePicker, SaveSaAsync, () => CloseEditor(returnFocus: true));
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static KeyEditorLabels EditorLabels()
    {
        var label = SettingsStrings.EditorLabel(ProviderWord, "service account JSON");
        return new KeyEditorLabels(
            label,
            label,
            SettingsStrings.SaveCredentialName(ProviderWord, NounWord),
            SettingsStrings.CancelEntryName(ProviderWord, NounWord),
            SettingsStrings.VertexSaEditorHelper,
            SettingsStrings.SaveServiceAccount);
    }

    [RelayCommand]
    private void BeginClear()
    {
        Message = null;
        ErrorMessage = null;
        IsConfirmingClear = true;
        RequestFocus(VertexRowFocusKeys.Keep);
    }

    [RelayCommand]
    private void KeepSa()
    {
        IsConfirmingClear = false;
        ErrorMessage = null;
        RequestFocus(VertexRowFocusKeys.Clear);
    }

    [RelayCommand]
    private async Task ConfirmClearAsync(CancellationToken cancellationToken)
    {
        IsClearing = true;
        try
        {
            await _secrets.DeleteAsync(SecretSlot.VertexServiceAccount, cancellationToken);
            ApplyCleared();
        }
        catch (SecretStoreException)
        {
            _logger.LogWarning("Could not clear the Vertex AI service account.");
            ErrorMessage = SettingsStrings.VertexClearFailed;
            RequestFocus(VertexRowFocusKeys.ConfirmClear);
        }
        finally
        {
            IsClearing = false;
        }
    }

    private void ApplyCleared()
    {
        IsConfirmingClear = false;
        ErrorMessage = null;
        ChipKind = VertexSaChipKind.NotSaved;
        Message = SettingsStrings.CredentialCleared(ProviderWord, NounWord);
        RequestFocus(VertexRowFocusKeys.Primary);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveSaAsync(VertexSaEditorViewModel editor, CancellationToken cancellationToken)
    {
        var json = editor.TakeJson();
        if (string.IsNullOrWhiteSpace(json))
        {
            editor.Error = SettingsStrings.VertexServiceAccountMissing;
            RequestFocus(VertexRowFocusKeys.Editor);
            return;
        }

        editor.IsSaving = true;
        editor.Error = null;
        try
        {
            await _credentials.SaveServiceAccountJsonAsync(json, cancellationToken);
            ApplySaSaved();
        }
        catch (SecretStoreException)
        {
            _logger.LogWarning("Could not save the Vertex AI service account.");
            editor.ClearPaste();
            editor.Error = SettingsStrings.VertexServiceAccountInvalid;
            RequestFocus(VertexRowFocusKeys.Editor);
        }
        finally
        {
            editor.IsSaving = false;
        }
    }

    private void ApplySaSaved()
    {
        CloseEditor(returnFocus: false);
        ChipKind = VertexSaChipKind.Saved;
        ErrorMessage = null;
        Message = SettingsStrings.CredentialSaved(ProviderWord, NounWord);
        RequestFocus(VertexRowFocusKeys.Primary);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ValidateProjectId()
    {
        _projectIdLive = true;
        ProjectIdError = string.IsNullOrWhiteSpace(ProjectId) ? SettingsStrings.VertexProjectIdEmpty : null;
    }

    [RelayCommand]
    private void ValidateLocation()
    {
        _locationLive = true;
        LocationError = string.IsNullOrWhiteSpace(Location) ? SettingsStrings.VertexLocationEmpty : null;
    }

    [RelayCommand]
    private void Discard()
    {
        _projectIdLive = false;
        _locationLive = false;
        ProjectIdError = null;
        LocationError = null;
        SaveBanner = null;
        ProjectId = _savedProjectId;
        Location = _savedLocation;
        RequestFocus(VertexRowFocusKeys.ProjectId);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        ValidateProjectId();
        ValidateLocation();
        ErrorMessage = HasSa ? null : SettingsStrings.VertexServiceAccountMissing;

        if (FocusFirstError())
        {
            return;
        }

        IsSaving = true;
        try
        {
            var next = new VertexUserSettings(Normalize(ProjectId), Normalize(Location));
            await _store.SaveVertexUserSettingsAsync(next, cancellationToken);
            _state.Update(next);
            ApplySaved(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the Vertex AI user settings.");
            SaveBanner = new BannerViewModel(new BannerContent
            {
                Severity = BannerSeverity.Error,
                Title = SettingsStrings.SaveFailedTitle,
                Message = SettingsStrings.SaveFailedMessage,
            });
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSave() => IsDirty && !IsSaving;

    private void ApplySaved(VertexUserSettings next)
    {
        _savedProjectId = next.ProjectId;
        _savedLocation = next.Location;
        ProjectId = next.ProjectId;
        Location = next.Location;
        SaveBanner = new BannerViewModel(new BannerContent
        {
            Severity = BannerSeverity.Success,
            Title = SettingsStrings.SavedTitle,
        });
        OnPropertyChanged(nameof(IsDirty));
        SaveCommand.NotifyCanExecuteChanged();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool FocusFirstError()
    {
        if (ProjectIdError is not null)
        {
            RequestFocus(VertexRowFocusKeys.ProjectId);
            return true;
        }

        if (LocationError is not null)
        {
            RequestFocus(VertexRowFocusKeys.Location);
            return true;
        }

        if (ErrorMessage is not null)
        {
            RequestFocus(VertexRowFocusKeys.Primary);
            return true;
        }

        return false;
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
