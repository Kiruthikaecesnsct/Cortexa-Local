using System.Text;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
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
    public required CollectorProvider Provider { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string ShortName { get; init; }

    public required string RunsName { get; init; }
}

public sealed partial class AiKeyRowViewModel : FocusableViewModel
{
    private readonly AiKeyRowDescriptor _descriptor;
    private readonly SettingsService _settings;
    private readonly ILogger _logger;

    public AiKeyRowViewModel(AiKeyRowDescriptor descriptor, SettingsService settings, ILogger logger)
    {
        _descriptor = descriptor;
        _settings = settings;
        _logger = logger;
        RequiresKey = AiKeySlots.For(descriptor.Provider) is not null;
        ChipKind = RequiresKey ? KeyChipKind.Checking : KeyChipKind.NoKeyNeeded;
    }

    public event EventHandler? EditorStateChanged;

    public bool RequiresKey { get; }

    public string Name => _descriptor.Name;

    public string Description => _descriptor.Description;

    public bool IsChecking => ChipKind == KeyChipKind.Checking;

    public bool HasKey => ChipKind == KeyChipKind.Set;

    public bool ShowActions => RequiresKey && !IsChecking && !IsConfirmingClear;

    public bool ShowStatusHelper => ChipKind == KeyChipKind.Unknown;

    public string StatusHelper => SettingsStrings.StatusUnknownHelper;

    public string ChipText => ChipKind switch
    {
        KeyChipKind.Set => SettingsStrings.ChipSet,
        KeyChipKind.NotSet => SettingsStrings.ChipNotSet,
        KeyChipKind.NoKeyNeeded => SettingsStrings.ChipNoKey,
        KeyChipKind.Unknown => SettingsStrings.ChipUnknown,
        _ => SettingsStrings.ChipChecking,
    };

    public string ChipAutomationName => SettingsStrings.ChipName(_descriptor.ShortName, ChipText.TrimEnd('…'));

    public string ActionLabel => HasKey ? SettingsStrings.Replace : SettingsStrings.AddKey;

    public string ActionAutomationName =>
        HasKey ? SettingsStrings.ReplaceName(_descriptor.ShortName) : SettingsStrings.AddName(_descriptor.ShortName);

    public string ClearAutomationName => SettingsStrings.ClearName(_descriptor.ShortName);

    public string ConfirmText => SettingsStrings.ConfirmClear(_descriptor.ShortName, _descriptor.RunsName);

    public string ConfirmAutomationName => SettingsStrings.ConfirmClearName(_descriptor.ShortName);

    public string KeepAutomationName => SettingsStrings.KeepName(_descriptor.ShortName);

    public string ConfirmLabel => IsClearing ? SettingsStrings.Clearing : SettingsStrings.ClearKey;

    public bool IsConfirmEnabled => !IsClearing;

    public bool HasEditor => Editor is not null;

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
            var present = await _settings.HasAiKeyAsync(_descriptor.Provider, cancellationToken);
            ChipKind = present ? KeyChipKind.Set : KeyChipKind.NotSet;
        }
        catch (Exception ex) when (ex is SecretStoreException or OperationCanceledException)
        {
            _logger.LogWarning("Could not read the {Provider} key status.", _descriptor.Provider);
            ChipKind = KeyChipKind.Unknown;
        }
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
            new KeyEditorLabels(
                SettingsStrings.EditorLabel(_descriptor.ShortName),
                SettingsStrings.EditorLabel(_descriptor.ShortName),
                SettingsStrings.SaveKeyName(_descriptor.ShortName),
                SettingsStrings.CancelKeyName(_descriptor.ShortName)),
            SaveKeyAsync,
            () => CloseEditor(returnFocus: true));
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
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
            await _settings.ClearAiKeyAsync(_descriptor.Provider, cancellationToken);
            ApplyCleared();
        }
        catch (SecretStoreException)
        {
            _logger.LogWarning("Could not clear the {Provider} key.", _descriptor.Provider);
            ErrorMessage = SettingsStrings.KeyClearFailed;
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
        Message = SettingsStrings.KeyCleared(_descriptor.ShortName);
        RequestFocus(KeyRowFocusKeys.Primary);
    }

    private async Task SaveKeyAsync(KeyEditorViewModel editor, CancellationToken cancellationToken)
    {
        var key = editor.TakeKey();
        var invalid = ValidateKey(key);
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
            await _settings.SetAiKeyAsync(_descriptor.Provider, key, cancellationToken);
            ApplySaved();
        }
        catch (SecretStoreException)
        {
            _logger.LogWarning("Could not save the {Provider} key.", _descriptor.Provider);
            editor.ClearPassword();
            editor.Error = SettingsStrings.KeySaveFailed;
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
        Message = SettingsStrings.KeySaved(_descriptor.ShortName);
        RequestFocus(KeyRowFocusKeys.Primary);
    }

    private static string? ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return SettingsStrings.PasteFirst;
        }

        return Encoding.UTF8.GetByteCount(key) > CredentialManagerStore.MaxBlobBytes
            ? SettingsStrings.KeyTooLong
            : null;
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
