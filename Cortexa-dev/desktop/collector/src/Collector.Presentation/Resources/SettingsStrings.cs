namespace Collector.Presentation.Resources;

public static class SettingsStrings
{
    public const string ConnectionsTitle = "Connections";
    public const string ConnectionsIntro = "Where the app sends sign-in and uploads.";
    public const string GatewayLabel = "_Gateway URL";
    public const string GatewayName = "Gateway URL";
    public const string GatewayHelper = "Cortexa API gateway. Used for sign-in.";

    public const string GatewayHint =
        "You're signed in through the current gateway. Saving a different gateway URL signs you out.";

    public const string CollectorLabel = "C_ollector server URL";
    public const string CollectorName = "Collector server URL";
    public const string CollectorHelper = "Receives the knowledge you upload.";
    public const string Save = "Sa_ve endpoints";
    public const string SaveName = "Save endpoints";
    public const string Saving = "Saving…";
    public const string SavingName = "Saving settings";
    public const string Discard = "Discard changes";
    public const string DiscardName = "Discard endpoint changes";
    public const string Unsaved = "Unsaved changes";
    public const string SavedTitle = "Settings saved.";
    public const string SavedSignedOut = "You were signed out because the gateway changed. Sign in again.";
    public const string SaveFailedTitle = "Couldn't save settings.";

    public const string SaveFailedMessage =
        "The settings file could not be written. Check that you have write access to %LOCALAPPDATA%\\Cortexa\\Collector, then try again.";

    public const string GatewayEmpty = "Enter the gateway URL.";
    public const string GatewayNotAbsolute = "Enter a full URL, like https://gateway.example.com.";
    public const string CollectorEmpty = "Enter the collector server URL.";
    public const string CollectorNotAbsolute = "Enter a full URL, like https://collector.example.com.";
    public const string UrlHttpNotLocal = "Use https. Plain http is only allowed for localhost.";
    public const string UrlOtherScheme = "Use an https:// address.";

    public const string KeysTitle = "AI provider keys";

    public const string KeysIntro =
        "Keys are stored in Windows Credential Manager on this computer. They are never shown again after you save them.";

    public const string ClaudeName = "Claude (Anthropic API)";
    public const string ClaudeDescription = "Used for \"Claude direct\" runs.";
    public const string GeminiName = "Google Gemini";
    public const string GeminiDescription = "Used for \"Gemini direct\" runs.";
    public const string BedrockName = "Claude on AWS Bedrock";
    public const string BedrockDescription = "Uses AWS SSO, no key.";
    public const string BedrockConnectedChip = "Connected";
    public const string BedrockNotConnectedChip = "Not connected";
    public const string BedrockConnect = "Connect via SSO";
    public const string BedrockConnecting = "Connecting…";
    public const string BedrockConnectName = "Connect Bedrock via AWS SSO";
    public const string BedrockDisconnect = "Disconnect";
    public const string BedrockDisconnecting = "Disconnecting…";
    public const string BedrockDisconnectName = "Disconnect Bedrock AWS SSO session";
    public const string BedrockConnectedMessage = "Connected to AWS. \"Bedrock\" runs are ready to use.";
    public const string BedrockDisconnectedMessage = "Disconnected. Sign in again to use Bedrock runs.";
    public const string BedrockConnectFailed = "Couldn't connect to AWS SSO. Try again.";
    public const string BedrockDisconnectFailed = "Couldn't disconnect the AWS SSO session. Try again.";
    public const string BedrockChipAutomationName = "Bedrock AWS SSO status";
    public const string ChipSet = "Set";
    public const string ChipNotSet = "Not set";
    public const string ChipNoKey = "No key needed";
    public const string ChipChecking = "Checking…";
    public const string ChipUnknown = "Unknown";
    public const string StatusUnknownHelper = "Couldn't read the key status. Reopen Settings to try again.";
    public const string AddKey = "Add key";
    public const string Replace = "Replace";
    public const string Clear = "Clear";
    public const string EditorHelper = "Paste the key from your provider's console. It won't be shown again.";
    public const string SaveKey = "Save key";
    public const string PasteFirst = "Paste the key first.";

    public const string KeyTooLong =
        "This key is too long to store (limit 2,560 bytes). Check that you pasted only the key.";

    public const string KeySaveFailed = "Couldn't save the key to Windows Credential Manager. Try again.";
    public const string ClearKey = "Clear key";
    public const string KeepKey = "Keep key";
    public const string Clearing = "Clearing…";
    public const string KeyClearFailed = "Couldn't remove the key from Windows Credential Manager. Try again.";
    public const string Cancel = "Cancel";

    public static string EditorLabel(string provider) => $"New {provider} API key";

    public static string KeySaved(string provider) => $"{provider} key saved.";

    public static string KeyCleared(string provider) => $"{provider} key cleared.";

    public static string ConfirmClear(string provider, string runs) =>
        $"Remove the {provider} key from this computer? {runs} runs won't work until you add a key again.";

    public static string ChipName(string provider, string state) => $"{provider} key: {state}";

    public static string AddName(string provider) => $"Add {provider} key";

    public static string ReplaceName(string provider) => $"Replace {provider} key";

    public static string ClearName(string provider) => $"Clear {provider} key";

    public static string SaveKeyName(string provider) => $"Save {provider} key";

    public static string CancelKeyName(string provider) => $"Cancel {provider} key entry";

    public static string ConfirmClearName(string provider) => $"Confirm clear {provider} key";

    public static string KeepName(string provider) => $"Keep {provider} key";
}
