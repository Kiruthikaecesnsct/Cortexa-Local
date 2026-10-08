using System.Text;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Infrastructure.Secrets;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public enum KeyChipKind
{
    Checking,
    Set,
    NotSet,
    NoKeyNeeded,
    Unknown,
}

public sealed record AiKeyRowDescriptor
{
    public required SecretSlot? Slot { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string ShortName { get; init; }

    public required string RunsName { get; init; }

    public string? Hint { get; init; }

    public CredentialWords Words { get; init; } = CredentialWords.ApiKey;
}

public sealed partial class AiKeyRowViewModel : FocusableViewModel
{
    private readonly AiKeyRowDescriptor _descriptor;
    private readonly SettingsService _settings;
    private readonly ILogger _logger;
    private bool _focusWhenReady;

    public AiKeyRowViewModel(AiKeyRowDescriptor descriptor, SettingsService settings, ILogger logger)
    {
        _descriptor = descriptor;
        _settings = settings;
        _logger = logger;
        RequiresKey = descriptor.Slot is not null;
        ChipKind = RequiresKey ? KeyChipKind.Checking : KeyChipKind.NoKeyNeeded;
    }

    public event EventHandler? EditorStateChanged;

    public bool RequiresKey { get; }

    public string Name => _descriptor.Name;

    public string Description => _descriptor.Description;

    public string? Hint => _descriptor.Hint;

    public bool HasHint => !string.IsNullOrEmpty(_descriptor.Hint);

    public string KeepLabel => Words.KeepLabel;

    public bool IsChecking => ChipKind == KeyChipKind.Checking;

    public bool HasKey => ChipKind == KeyChipKind.Set;

    public bool ShowActions => RequiresKey && !IsChecking && !IsConfirmingClear;

    public bool ShowStatusHelper => ChipKind == KeyChipKind.Unknown;

    public string StatusHelper => Words.StatusUnknown;

    public string ChipText => ChipKind switch
    {
        KeyChipKind.Set => SettingsStrings.ChipSet,
        KeyChipKind.NotSet => SettingsStrings.ChipNotSet,
        KeyChipKind.NoKeyNeeded => SettingsStrings.ChipNoKey,
        KeyChipKind.Unknown => SettingsStrings.ChipUnknown,
        _ => SettingsStrings.ChipChecking,
    };

    public string ChipAutomationName => SettingsStrings.ChipName(_descriptor.ShortName, Words.Noun, ChipText.TrimEnd('…'));

    public string ActionLabel => HasKey ? SettingsStrings.Replace : Words.AddLabel;

    public string ActionAutomationName =>
        HasKey
            ? SettingsStrings.ReplaceName(_descriptor.ShortName, Words.Noun)
            : SettingsStrings.AddName(_descriptor.ShortName, Words.Noun);

    public string ClearAutomationName => SettingsStrings.ClearName(_descriptor.ShortName, Words.Noun);

    public string ConfirmText => SettingsStrings.ConfirmClear(_descriptor.ShortName, _descriptor.RunsName, Words.Noun);

    public string ConfirmAutomationName => SettingsStrings.ConfirmClearName(_descriptor.ShortName, Words.Noun);

    public string KeepAutomationName => SettingsStrings.KeepName(_descriptor.ShortName, Words.Noun);

    public string ConfirmLabel => IsClearing ? SettingsStrings.Clearing : Words.ClearLabel;

    public bool IsConfirmEnabled => !IsClearing;

    public bool HasEditor => Editor is not null;

    private CredentialWords Words => _descriptor.Words;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsChecking),
        nameof(HasKey),
        nameof(ShowActions),
        nameof(ShowStatusHelper),
        nameof(ChipText),
        nameof(ChipAutomationName),
        nameof(ActionLabel),
        nameof(ActionAutomationName))]
    public partial KeyChipKind ChipKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    public partial KeyEditorViewModel? Editor { get; set; }

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

    public async Task LoadStatusAsync(CancellationToken cancellationToken)
    {
        if (!RequiresKey || ChipKind is KeyChipKind.Set or KeyChipKind.NotSet)
        {
            return;
        }

        ChipKind = KeyChipKind.Checking;
        try
        {
            var present = await _settings.HasSecretAsync(_descriptor.Slot!.Value, cancellationToken);
            ChipKind = present ? KeyChipKind.Set : KeyChipKind.NotSet;
        }
        catch (Exception ex) when (ex is SecretStoreException or OperationCanceledException)
        {
            _logger.LogWarning("Could not read the {Slot} {Noun} status.", _descriptor.Slot, Words.Noun);
            ChipKind = KeyChipKind.Unknown;
        }

        ReleasePendingFocus();
    }

    public void FocusPrimary()
    {
        if (IsChecking)
        {
            _focusWhenReady = true;
            return;
        }

        RequestFocus(KeyRowFocusKeys.Primary);
    }

    private void ReleasePendingFocus()
    {
        if (!_focusWhenReady || IsChecking)
        {
            return;
        }

        _focusWhenReady = false;
        RequestFocus(KeyRowFocusKeys.Primary);
    }

    public void CloseEditor(bool returnFocus)
    {
        if (Editor is null)
        {
            return;
        }

        Editor.ClearPassword();
        Editor = null;
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
        if (returnFocus)
        {
            RequestFocus(KeyRowFocusKeys.Primary);
        }
    }

    [RelayCommand]
    private void OpenEditor()
    {
        Message = null;
        ErrorMessage = null;
        Editor = new KeyEditorViewModel(
            EditorLabels(),
            SaveKeyAsync,
            () => CloseEditor(returnFocus: true));
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private KeyEditorLabels EditorLabels()
    {
        var label = SettingsStrings.EditorLabel(_descriptor.ShortName, Words.EditorNoun);
        return new KeyEditorLabels(
            label,
            label,
            SettingsStrings.SaveCredentialName(_descriptor.ShortName, Words.Noun),
            SettingsStrings.CancelEntryName(_descriptor.ShortName, Words.Noun),
            Words.EditorHelper,
            Words.SaveLabel);
    }

    [RelayCommand]
    private void BeginClear()
    {
        Message = null;
        ErrorMessage = null;
        IsConfirmingClear = true;
        RequestFocus(KeyRowFocusKeys.Keep);
    }

    [RelayCommand]
    private void KeepKey()
    {
        IsConfirmingClear = false;
        ErrorMessage = null;
        RequestFocus(KeyRowFocusKeys.Clear);
    }

    [RelayCommand]
    private async Task ConfirmClearAsync(CancellationToken cancellationToken)
    {
        IsClearing = true;
        try
        {
            await _settings.ClearSecretAsync(_descriptor.Slot!.Value, cancellationToken);
            ApplyCleared();
        }
        catch (SecretStoreException)
        {
            _logger.LogWarning("Could not clear the {Slot} {Noun}.", _descriptor.Slot, Words.Noun);
            ErrorMessage = Words.ClearFailed;
            RequestFocus(KeyRowFocusKeys.ConfirmClear);
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
        ChipKind = KeyChipKind.NotSet;
        Message = SettingsStrings.CredentialCleared(_descriptor.ShortName, Words.Noun);
        RequestFocus(KeyRowFocusKeys.Primary);
    }

    private async Task SaveKeyAsync(KeyEditorViewModel editor, CancellationToken cancellationToken)
    {
        var key = editor.TakeKey();
        var invalid = ValidateKey(key, Words);
        if (invalid is not null)
        {
            editor.Error = invalid;
            RequestFocus(KeyRowFocusKeys.Editor);
            return;
        }

        editor.IsSaving = true;
        editor.Error = null;
        try
        {
            await _settings.SetSecretAsync(_descriptor.Slot!.Value, key, cancellationToken);
            ApplySaved();
        }
        catch (SecretStoreException)
        {
            _logger.LogWarning("Could not save the {Slot} {Noun}.", _descriptor.Slot, Words.Noun);
            editor.ClearPassword();
            editor.Error = Words.SaveFailed;
            RequestFocus(KeyRowFocusKeys.Editor);
        }
        finally
        {
            editor.IsSaving = false;
        }
    }

    private void ApplySaved()
    {
        CloseEditor(returnFocus: false);
        ChipKind = KeyChipKind.Set;
        ErrorMessage = null;
        Message = SettingsStrings.CredentialSaved(_descriptor.ShortName, Words.Noun);
        RequestFocus(KeyRowFocusKeys.Primary);
    }

    private static string? ValidateKey(string key, CredentialWords words)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return words.PasteFirst;
        }

        return Encoding.UTF8.GetByteCount(key) > CredentialManagerStore.MaxBlobBytes
            ? words.TooLong
            : SecretInputRules.Validate(key);
    }
}

public static class KeyRowFocusKeys
{
    public const string Primary = "Primary";
    public const string Clear = "Clear";
    public const string Keep = "Keep";
    public const string ConfirmClear = "ConfirmClear";
    public const string Editor = "Editor";
}
