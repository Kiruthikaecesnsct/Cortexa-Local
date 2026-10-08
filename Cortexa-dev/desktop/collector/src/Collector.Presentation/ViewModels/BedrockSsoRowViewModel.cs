using Collector.Application.Ports;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public enum BedrockSsoChipKind
{
    Checking,
    Connected,
    NotConnected,
    Unknown,
}

public static class BedrockRowFocusKeys
{
    public const string Primary = "Primary";
}

public sealed partial class BedrockSsoRowViewModel : FocusableViewModel
{
    private readonly IBedrockSsoCredentials _credentials;
    private readonly ILogger _logger;

    public BedrockSsoRowViewModel(IBedrockSsoCredentials credentials, ILogger logger)
    {
        _credentials = credentials;
        _logger = logger;
    }

    public string Name => SettingsStrings.BedrockName;

    public string Description => SettingsStrings.BedrockDescription;

    public bool IsChecking => ChipKind == BedrockSsoChipKind.Checking;

    public bool IsConnected => ChipKind == BedrockSsoChipKind.Connected;

    public bool ShowConnect => !IsConnected && !IsChecking && !IsConnecting;

    public bool ShowDisconnect => IsConnected && !IsDisconnecting;

    public bool ShowStatusHelper => ChipKind == BedrockSsoChipKind.Unknown;

    public string StatusHelper => SettingsStrings.StatusUnknownHelper;

    public string ChipText => ChipKind switch
    {
        BedrockSsoChipKind.Connected => SettingsStrings.BedrockConnectedChip,
        BedrockSsoChipKind.NotConnected => SettingsStrings.BedrockNotConnectedChip,
        BedrockSsoChipKind.Unknown => SettingsStrings.ChipUnknown,
        _ => SettingsStrings.ChipChecking,
    };

    public string ChipAutomationName => SettingsStrings.BedrockChipAutomationName;

    public string ConnectLabel => IsConnecting ? SettingsStrings.BedrockConnecting : SettingsStrings.BedrockConnect;

    public string DisconnectLabel => IsDisconnecting ? SettingsStrings.BedrockDisconnecting : SettingsStrings.BedrockDisconnect;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsChecking),
        nameof(IsConnected),
        nameof(ShowConnect),
        nameof(ShowDisconnect),
        nameof(ShowStatusHelper),
        nameof(ChipText))]
    public partial BedrockSsoChipKind ChipKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnect), nameof(ConnectLabel))]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDisconnect), nameof(DisconnectLabel))]
    public partial bool IsDisconnecting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public async Task LoadStatusAsync(CancellationToken cancellationToken)
    {
        if (ChipKind is BedrockSsoChipKind.Connected or BedrockSsoChipKind.NotConnected)
        {
            return;
        }

        ChipKind = BedrockSsoChipKind.Checking;
        try
        {
            var status = await _credentials.GetStatusAsync(cancellationToken);
            ChipKind = status.IsConnected ? BedrockSsoChipKind.Connected : BedrockSsoChipKind.NotConnected;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the Bedrock AWS SSO status.");
            ChipKind = BedrockSsoChipKind.Unknown;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Message = null;
        ErrorMessage = null;
        IsConnecting = true;
        try
        {
            await _credentials.ConnectAsync(cancellationToken);
            ChipKind = BedrockSsoChipKind.Connected;
            Message = SettingsStrings.BedrockConnectedMessage;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not connect to AWS SSO for Bedrock.");
            ErrorMessage = SettingsStrings.BedrockConnectFailed;
        }
        finally
        {
            IsConnecting = false;
            RequestFocus(BedrockRowFocusKeys.Primary);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Message = null;
        ErrorMessage = null;
        IsDisconnecting = true;
        try
        {
            await _credentials.DisconnectAsync(cancellationToken);
            ChipKind = BedrockSsoChipKind.NotConnected;
            Message = SettingsStrings.BedrockDisconnectedMessage;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not disconnect the Bedrock AWS SSO session.");
            ErrorMessage = SettingsStrings.BedrockDisconnectFailed;
        }
        finally
        {
            IsDisconnecting = false;
            RequestFocus(BedrockRowFocusKeys.Primary);
        }
    }
}
