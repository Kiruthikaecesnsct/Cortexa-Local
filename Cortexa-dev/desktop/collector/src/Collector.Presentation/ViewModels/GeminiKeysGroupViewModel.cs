using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Automation;
using Collector.Application.Ai;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Infrastructure.Secrets;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public static class GeminiFocusKeys
{
    public const string AddKey = "GeminiAddKey";
    public const string AddKeyEmpty = "GeminiAddKeyEmpty";
}

public sealed partial class GeminiKeysGroupViewModel : FocusableViewModel
{
    private const int MaskDotCount = 12;

    private readonly SettingsService _settings;
    private readonly IGeminiKeyStatusStore _statusStore;
    private readonly ILogger _logger;
    private bool _loaded;

    public GeminiKeysGroupViewModel(SettingsService settings, IGeminiKeyStatusStore statusStore, ILogger logger)
    {
        _settings = settings;
        _statusStore = statusStore;
        _logger = logger;
        Items = [];
        _statusStore.Changed += (_, _) => UiThread.Post(SyncStatuses);
    }

    public event EventHandler? EditorStateChanged;

    public ObservableCollection<GeminiKeyItemViewModel> Items { get; }

    public bool IsEmpty => Items.Count == 0;

    public bool HasKeys => !IsEmpty;

    public bool HasEditor => Editor is not null;

    public string Description => SettingsStrings.GeminiKeysDescription;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    public partial KeyEditorViewModel? Editor { get; set; }

    [ObservableProperty]
    public partial string Announcement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AutomationLiveSetting AnnouncementSetting { get; set; } = AutomationLiveSetting.Polite;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            var summaries = await _settings.GetGeminiKeySummariesAsync(cancellationToken);
            ApplyLoaded(summaries);
            _loaded = true;
        }
        catch (SecretStoreException ex)
        {
            _logger.LogWarning(ex, "Could not load the Gemini keys.");
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
            FocusAddButton();
        }
    }

    public void MoveUp(GeminiKeyItemViewModel item) => _ = ReorderAsync(item, -1);

    public void MoveDown(GeminiKeyItemViewModel item) => _ = ReorderAsync(item, 1);

    public void BeginRemove(GeminiKeyItemViewModel item)
    {
        foreach (var other in Items)
        {
            if (other != item)
            {
                other.IsConfirmingRemove = false;
            }
        }

        item.ErrorMessage = null;
        item.IsConfirmingRemove = true;
        RequestFocus(item.KeepKey);
    }

    public void KeepRow(GeminiKeyItemViewModel item)
    {
        item.IsConfirmingRemove = false;
        item.ErrorMessage = null;
        RequestFocus(item.RemoveKey);
    }

    public async Task ConfirmRemoveAsync(GeminiKeyItemViewModel item, CancellationToken cancellationToken)
    {
        item.IsRemoving = true;
        item.ErrorMessage = null;
        try
        {
            await _settings.RemoveGeminiKeyAsync(item.Id, cancellationToken);
            ApplyRemoved(item);
        }
        catch (SecretStoreException ex)
        {
            _logger.LogWarning(ex, "Could not remove the Gemini key.");
            item.ErrorMessage = SettingsStrings.KeyClearFailed;
            RequestFocus(item.ConfirmRemoveKey);
        }
        finally
        {
            item.IsRemoving = false;
        }
    }

    public static string BuildPreview(string last4) => $"AIza{new string('·', MaskDotCount)}{last4}";

    [RelayCommand]
    private void OpenEditor()
    {
        Editor = new KeyEditorViewModel(EditorLabels(), SaveKeyAsync, () => CloseEditor(returnFocus: true));
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncStatuses()
    {
        foreach (var item in Items)
        {
            var next = _statusStore.GetStatus(item.Id);
            if (next == item.Status)
            {
                continue;
            }

            item.Status = next;
            Announce(SettingsStrings.GeminiKeyStatusAnnouncement(item.Position, item.StatusLabel));
        }
    }

    private void ApplyLoaded(IReadOnlyList<GeminiKeySummary> summaries)
    {
        Items.Clear();
        foreach (var summary in summaries)
        {
            Items.Add(CreateItem(summary));
        }

        Renumber();
        NotifyCountChanged();
    }

    private GeminiKeyItemViewModel CreateItem(GeminiKeySummary summary) =>
        new(summary.Id, summary.Last4, this) { Status = _statusStore.GetStatus(summary.Id) };

    private void ApplyRemoved(GeminiKeyItemViewModel item)
    {
        var index = Items.IndexOf(item);
        Items.Remove(item);
        Renumber();
        NotifyCountChanged();
        if (Items.Count == 0)
        {
            FocusAddButton();
            return;
        }

        var target = index < Items.Count ? Items[index] : Items[^1];
        RequestFocus(target.RemoveKey);
    }

    private void FocusAddButton() => RequestFocus(IsEmpty ? GeminiFocusKeys.AddKeyEmpty : GeminiFocusKeys.AddKey);

    private void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasKeys));
    }

    private void Renumber()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Position = index + 1;
            Items[index].IsFirst = index == 0;
            Items[index].IsLast = index == Items.Count - 1;
        }
    }

    private async Task ReorderAsync(GeminiKeyItemViewModel item, int delta)
    {
        var index = Items.IndexOf(item);
        var targetIndex = index + delta;
        if (item.IsMoving || targetIndex < 0 || targetIndex >= Items.Count)
        {
            return;
        }

        var partner = Items[targetIndex];
        item.IsMoving = true;
        partner.IsMoving = true;
        Items.Move(index, targetIndex);
        Renumber();
        try
        {
            await _settings.ReorderGeminiKeysAsync([.. Items.Select(row => row.Id)], CancellationToken.None);
            Announce(SettingsStrings.GeminiKeyMovedAnnouncement(item.Position));
        }
        catch (SecretStoreException ex)
        {
            _logger.LogWarning(ex, "Could not reorder the Gemini keys.");
            Items.Move(Items.IndexOf(item), index);
            Renumber();
            item.ErrorMessage = SettingsStrings.KeyClearFailed;
        }
        finally
        {
            item.IsMoving = false;
            partner.IsMoving = false;
            RequestFocus(LandingKey(item, delta));
        }
    }

    private static string LandingKey(GeminiKeyItemViewModel item, int delta) =>
        delta < 0
            ? (item.IsFirst ? item.DownKey : item.UpKey)
            : (item.IsLast ? item.UpKey : item.DownKey);

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
            var summary = await _settings.AddGeminiKeyAsync(key, cancellationToken);
            ApplyAdded(summary);
        }
        catch (SecretStoreException ex)
        {
            _logger.LogWarning(ex, "Could not save the Gemini key.");
            editor.ClearPassword();
            editor.Error = SettingsStrings.KeySaveFailed;
        }
        finally
        {
            editor.IsSaving = false;
        }
    }

    private string? ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return SettingsStrings.PasteFirst;
        }

        if (Encoding.UTF8.GetByteCount(key) > CredentialManagerStore.MaxBlobBytes)
        {
            return SettingsStrings.KeyTooLong;
        }

        var ruleError = SecretInputRules.Validate(key);
        if (ruleError is not null)
        {
            return ruleError;
        }

        var candidateLast4 = GeminiKey.Last4Of(key.Trim());
        return Items.Any(row => row.Last4 == candidateLast4) ? SettingsStrings.DuplicateKey : null;
    }

    private void ApplyAdded(GeminiKeySummary summary)
    {
        Items.Add(CreateItem(summary));
        Renumber();
        NotifyCountChanged();
        CloseEditor(returnFocus: true);
    }

    private KeyEditorLabels EditorLabels() => new(
        SettingsStrings.AddGeminiKeyEditorLabel,
        SettingsStrings.AddGeminiKeyEditorLabel,
        SettingsStrings.SaveGeminiKeyName,
        SettingsStrings.CancelGeminiKeyName,
        SettingsStrings.EditorHelper,
        SettingsStrings.SaveKey);

    private void Announce(string text)
    {
        Announcement = string.Empty;
        Announcement = text;
    }
}

public sealed partial class GeminiKeyItemViewModel : ObservableObject
{
    private readonly GeminiKeysGroupViewModel _group;

    public GeminiKeyItemViewModel(string id, string last4, GeminiKeysGroupViewModel group)
    {
        Id = id;
        Last4 = last4;
        _group = group;
        Preview = GeminiKeysGroupViewModel.BuildPreview(last4);
    }

    public string Id { get; }

    public string Last4 { get; }

    public string Preview { get; }

    public string UpKey => $"GeminiUp:{Id}";

    public string DownKey => $"GeminiDown:{Id}";

    public string RemoveKey => $"GeminiRemove:{Id}";

    public string ConfirmRemoveKey => $"GeminiConfirmRemove:{Id}";

    public string KeepKey => $"GeminiKeep:{Id}";

    public bool ShowRowActions => !IsConfirmingRemove;

    public bool CanMoveUp => !IsFirst && !IsMoving;

    public bool CanMoveDown => !IsLast && !IsMoving;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string StatusLabel => Status switch
    {
        GeminiKeyStatus.Ok => SettingsStrings.StatusOk,
        GeminiKeyStatus.RateLimited => SettingsStrings.StatusRateLimited,
        GeminiKeyStatus.Rejected => SettingsStrings.StatusRejected,
        _ => SettingsStrings.StatusUntested,
    };

    public string PreviewAutomationName => SettingsStrings.GeminiKeyPreviewName(Position, Last4);

    public string StatusAutomationName => SettingsStrings.GeminiStatusName(Position, StatusLabel);

    public string UpAutomationName => SettingsStrings.GeminiMoveUpName(Position);

    public string DownAutomationName => SettingsStrings.GeminiMoveDownName(Position);

    public string RemoveAutomationName => SettingsStrings.GeminiRemoveName(Position);

    public string ConfirmRemoveText => SettingsStrings.GeminiRemoveConfirm(Position);

    public string ConfirmRemoveLabel => IsRemoving ? SettingsStrings.Removing : SettingsStrings.Remove;

    public bool IsConfirmEnabled => !IsRemoving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(PreviewAutomationName),
        nameof(StatusAutomationName),
        nameof(UpAutomationName),
        nameof(DownAutomationName),
        nameof(RemoveAutomationName),
        nameof(ConfirmRemoveText))]
    public partial int Position { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveUp))]
    public partial bool IsFirst { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveDown))]
    public partial bool IsLast { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(StatusAutomationName))]
    public partial GeminiKeyStatus Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRowActions))]
    public partial bool IsConfirmingRemove { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmRemoveLabel), nameof(IsConfirmEnabled))]
    public partial bool IsRemoving { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveUp), nameof(CanMoveDown))]
    public partial bool IsMoving { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [RelayCommand]
    private void MoveUp() => _group.MoveUp(this);

    [RelayCommand]
    private void MoveDown() => _group.MoveDown(this);

    [RelayCommand]
    private void BeginRemove() => _group.BeginRemove(this);

    [RelayCommand]
    private void Keep() => _group.KeepRow(this);

    [RelayCommand]
    private Task ConfirmRemoveAsync(CancellationToken cancellationToken) => _group.ConfirmRemoveAsync(this, cancellationToken);
}
