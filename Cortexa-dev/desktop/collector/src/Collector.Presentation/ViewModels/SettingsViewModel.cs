using System.IO;
using Collector.Application.Auth;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class SettingsViewModel : FocusableViewModel, INavigationAware
{
    private readonly SettingsService _settings;
    private readonly ISessionState _session;
    private readonly ISignInService _signIn;
    private readonly ILogger<SettingsViewModel> _logger;
    private EndpointSettings _saved;
    private bool _gatewayLive;
    private bool _collectorLive;

    public SettingsViewModel(
        SettingsService settings,
        ISessionState session,
        ISignInService signIn,
        IBedrockSsoCredentials bedrockSso,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _session = session;
        _signIn = signIn;
        _logger = logger;
        _saved = settings.GetEndpoints();
        GatewayUrl = _saved.GatewayUrl;
        CollectorServerUrl = _saved.CollectorServerUrl;
        Rows = CreateRows();
        foreach (var row in AllRows)
        {
            row.EditorStateChanged += OnEditorStateChanged;
        }

        BedrockRow = new BedrockSsoRowViewModel(bedrockSso, logger);
        session.Changed += (_, _) => UiThread.Post(() => OnPropertyChanged(nameof(ShowGatewayHint)));
    }

    public IReadOnlyList<AiKeyRowViewModel> Rows { get; }

    public BedrockSsoRowViewModel BedrockRow { get; }

    public bool IsDirty => Normalize(GatewayUrl) != _saved.GatewayUrl || Normalize(CollectorServerUrl) != _saved.CollectorServerUrl;

    public bool ShowGatewayHint =>
        _session.Current == SessionState.SignedIn && Normalize(GatewayUrl) != _saved.GatewayUrl;

    public bool AreFieldsEnabled => !IsSaving;

    public string SaveLabel => IsSaving ? SettingsStrings.Saving : SettingsStrings.Save;

    public bool IsSaveDefault => !AllRows.Any(row => row.HasEditor);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(ShowGatewayHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveEndpointsCommand))]
    public partial string GatewayUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveEndpointsCommand))]
    public partial string CollectorServerUrl { get; set; }

    public string GatewayHelp => GatewayError ?? SettingsStrings.GatewayHelper;

    public string CollectorHelp => CollectorError ?? SettingsStrings.CollectorHelper;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GatewayHelp))]
    public partial string? GatewayError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollectorHelp))]
    public partial string? CollectorError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreFieldsEnabled), nameof(SaveLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveEndpointsCommand))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? SaveBanner { get; set; }

    public void OnNavigatedTo()
    {
        if (!IsDirty)
        {
            ReloadFromStore();
        }

        foreach (var row in AllRows)
        {
            _ = row.LoadStatusAsync(CancellationToken.None);
        }

        _ = BedrockRow.LoadStatusAsync(CancellationToken.None);

        FocusInitial();
    }

    public void OnNavigatedFrom()
    {
        foreach (var row in AllRows)
        {
            row.CloseEditor(returnFocus: false);
        }
    }

    partial void OnGatewayUrlChanged(string value)
    {
        SaveBanner = null;
        if (_gatewayLive)
        {
            GatewayError = EndpointDisplay.ErrorFor(value, EndpointField.GatewayUrl);
        }
    }

    partial void OnCollectorServerUrlChanged(string value)
    {
        SaveBanner = null;
        if (_collectorLive)
        {
            CollectorError = EndpointDisplay.ErrorFor(value, EndpointField.CollectorServerUrl);
        }
    }

    [RelayCommand]
    private void ValidateGateway()
    {
        GatewayError = EndpointDisplay.ErrorFor(GatewayUrl, EndpointField.GatewayUrl);
        _gatewayLive |= GatewayError is not null;
    }

    [RelayCommand]
    private void ValidateCollector()
    {
        CollectorError = EndpointDisplay.ErrorFor(CollectorServerUrl, EndpointField.CollectorServerUrl);
        _collectorLive |= CollectorError is not null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveEndpointsAsync(CancellationToken cancellationToken)
    {
        ValidateGateway();
        ValidateCollector();
        if (FocusFirstError())
        {
            return;
        }

        IsSaving = true;
        try
        {
            await PersistAsync(new EndpointSettings(Normalize(GatewayUrl), Normalize(CollectorServerUrl)), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the user settings file.");
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

    [RelayCommand]
    private void Discard()
    {
        _gatewayLive = false;
        _collectorLive = false;
        GatewayError = null;
        CollectorError = null;
        GatewayUrl = _saved.GatewayUrl;
        CollectorServerUrl = _saved.CollectorServerUrl;
        RequestFocus(SettingsFocusKeys.Gateway);
    }

    private async Task PersistAsync(EndpointSettings next, CancellationToken cancellationToken)
    {
        var result = await _settings.SaveEndpointsAsync(next, cancellationToken);
        if (!result.IsValid)
        {
            ShowRejected(result);
            return;
        }

        var gatewayChanged = next.GatewayUrl != _saved.GatewayUrl;
        _saved = next;
        GatewayUrl = next.GatewayUrl;
        CollectorServerUrl = next.CollectorServerUrl;
        var signedOut = gatewayChanged && await SignOutAsync(cancellationToken);
        SaveBanner = new BannerViewModel(new BannerContent
        {
            Severity = BannerSeverity.Success,
            Title = SettingsStrings.SavedTitle,
            Message = signedOut ? SettingsStrings.SavedSignedOut : null,
        });
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(ShowGatewayHint));
        SaveEndpointsCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> SignOutAsync(CancellationToken cancellationToken)
    {
        if (_session.Current != SessionState.SignedIn)
        {
            return false;
        }

        await _signIn.SignOutAsync(cancellationToken);
        return true;
    }

    private void ShowRejected(SettingsSaveResult result)
    {
        var gateway = result.Field == EndpointField.GatewayUrl;
        if (gateway)
        {
            GatewayError = EndpointDisplay.ErrorFor(GatewayUrl, EndpointField.GatewayUrl) ?? result.Reason;
            _gatewayLive = true;
        }
        else
        {
            CollectorError = EndpointDisplay.ErrorFor(CollectorServerUrl, EndpointField.CollectorServerUrl) ?? result.Reason;
            _collectorLive = true;
        }

        RequestFocus(gateway ? SettingsFocusKeys.Gateway : SettingsFocusKeys.Collector);
    }

    private bool FocusFirstError()
    {
        if (GatewayError is not null)
        {
            RequestFocus(SettingsFocusKeys.Gateway);
            return true;
        }

        if (CollectorError is null)
        {
            return false;
        }

        RequestFocus(SettingsFocusKeys.Collector);
        return true;
    }

    private void ReloadFromStore()
    {
        _saved = _settings.GetEndpoints();
        GatewayUrl = _saved.GatewayUrl;
        CollectorServerUrl = _saved.CollectorServerUrl;
        SaveEndpointsCommand.NotifyCanExecuteChanged();
    }

    private void OnEditorStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsSaveDefault));
        if (sender is not AiKeyRowViewModel { HasEditor: true } opened)
        {
            return;
        }

        foreach (var other in AllRows.Where(row => row != opened))
        {
            other.CloseEditor(returnFocus: false);
        }
    }

    private IEnumerable<AiKeyRowViewModel> AllRows => Rows;

    private void FocusInitial()
    {
        RequestFocus(SettingsFocusKeys.Gateway);
    }

    private AiKeyRowViewModel[] CreateRows() =>
    [
        new(
            new AiKeyRowDescriptor
            {
                Slot = SecretSlot.AnthropicApiKey,
                Name = SettingsStrings.ClaudeName,
                Description = SettingsStrings.ClaudeDescription,
                ShortName = "Claude",
                RunsName = "Claude direct runs",
            },
            _settings,
            _logger),
        new(
            new AiKeyRowDescriptor
            {
                Slot = SecretSlot.GeminiApiKey,
                Name = SettingsStrings.GeminiName,
                Description = SettingsStrings.GeminiDescription,
                ShortName = "Gemini",
                RunsName = "Gemini direct runs",
            },
            _settings,
            _logger),
    ];

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}

public static class SettingsFocusKeys
{
    public const string Gateway = "GatewayUrl";
    public const string Collector = "CollectorUrl";
}
