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

    public const string ClaudeName = "Claude";
    public const string ClaudeDescription = "Anthropic API key. Used when Claude is the AI model.";
    public const string GeminiName = "Gemini";
    public const string GeminiDescription = "Google AI API key. Used when Gemini is the AI model.";
    public const string BedrockName = "Amazon Bedrock";
    public const string BedrockDescription = "Signs in with AWS SSO. No key needed.";
    public const string BedrockConnectedChip = "Connected";
    public const string BedrockNotConnectedChip = "Not connected";
    public const string BedrockConnect = "Connect via SSO";
    public const string BedrockConnecting = "Connecting…";
    public const string BedrockConnectName = "Connect Bedrock via AWS SSO";
    public const string BedrockDisconnect = "Disconnect";
    public const string BedrockDisconnecting = "Disconnecting…";
    public const string BedrockDisconnectName = "Disconnect Bedrock AWS SSO session";
    public const string BedrockConnectedMessage = "Connected to AWS. Amazon Bedrock is ready to use.";
    public const string BedrockDisconnectedMessage = "Disconnected. Connect again to use Amazon Bedrock.";
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

    public const string ClaudeRunsName = "Extract knowledge with Claude";
    public const string GeminiRunsName = "Extract knowledge with Gemini";
    public const string SectionListName = "Settings sections";
    public const string AiModelTitle = "AI model";

    public const string AiModelIntro =
        "Choose the provider and model that Extract knowledge uses. Your choice is saved on this computer.";

    public const string ProviderOverline = "PROVIDER";
    public const string ProviderGroupName = "AI provider";
    public const string ProviderClaudeTitle = "Claude";
    public const string ProviderClaudeDescription = "Anthropic API, using your own key.";
    public const string ProviderGeminiTitle = "Gemini";
    public const string ProviderGeminiDescription = "Google AI API, using your own key.";
    public const string ProviderBedrockTitle = "Amazon Bedrock";
    public const string ProviderBedrockDescription = "Claude on AWS, signed in with AWS SSO.";
    public const string ChipKeySet = "Key set";
    public const string ChipKeyNotSet = "Key not set";

    public const string ModelLabel = "_Model";
    public const string ModelName = "Model";
    public const string ChoiceSaveFailedTitle = "Couldn't save your model choice.";
    public const string ModelNotListedTitle = "That model isn't available for this provider.";
    public const string ModelNotListedMessage = "Your previous choice is still in use.";
    public const string KeyWarningMessage = "Extract knowledge stays off until you add the key.";
    public const string BedrockWarningTitle = "Not connected to AWS SSO.";
    public const string BedrockWarningMessage = "Extract knowledge stays off until you connect Amazon Bedrock.";
    public const string ReadinessUnknownMessage = "Reopen Settings to try again.";
    public const string GoToKeys = "Go to AI provider keys";
    public const string GoToKeysName = "Open the AI provider keys section";


    public static string ProviderCardName(string provider, string status) => $"{provider}, {status}";

    public static string ModelHelper(string provider) =>
        $"Models available for {provider}. Changes apply to the next extraction.";

    public static string ModelsEmpty(string provider) =>
        $"No models are set up for {provider}. Check Ai:ModelCatalog in appsettings.json.";

    public static string ChoiceSaved(string provider, string model) =>
        $"Saved. Extract knowledge uses {provider} · {model}.";

    public static string KeyWarningTitle(string provider) => $"{provider} key not set.";

    public static string ReadinessUnknownTitle(string provider) => $"Couldn't check {provider} access.";


    public const string AddToken = "Add token";
    public const string SaveToken = "Save token";
    public const string ClearToken = "Clear token";
    public const string KeepToken = "Keep token";
    public const string TokenEditorHelper = "Paste the token from your account settings. It won't be shown again.";
    public const string PasteTokenFirst = "Paste the token first.";

    public const string TokenTooLong =
        "This token is too long to store (limit 2,560 bytes). Check that you pasted only the token.";

    public const string TokenSaveFailed = "Couldn't save the token to Windows Credential Manager. Try again.";
    public const string TokenClearFailed = "Couldn't remove the token from Windows Credential Manager. Try again.";
    public const string TokenStatusUnknownHelper = "Couldn't read the token status. Reopen Settings to try again.";

    public static string EditorLabel(string provider, string editorNoun) => $"New {provider} {editorNoun}";

    public static string CredentialSaved(string provider, string noun) => $"{provider} {noun} saved.";

    public static string CredentialCleared(string provider, string noun) => $"{provider} {noun} cleared.";

    public static string ConfirmClear(string provider, string runs, string noun) =>
        $"Remove the {provider} {noun} from this computer? {runs} won't work until you add a {noun} again.";

    public static string ChipName(string provider, string noun, string state) => $"{provider} {noun}: {state}";

    public static string AddName(string provider, string noun) => $"Add {provider} {noun}";

    public static string ReplaceName(string provider, string noun) => $"Replace {provider} {noun}";

    public static string ClearName(string provider, string noun) => $"Clear {provider} {noun}";

    public static string SaveCredentialName(string provider, string noun) => $"Save {provider} {noun}";

    public static string CancelEntryName(string provider, string noun) => $"Cancel {provider} {noun} entry";

    public static string ConfirmClearName(string provider, string noun) => $"Confirm clear {provider} {noun}";

    public static string KeepName(string provider, string noun) => $"Keep {provider} {noun}";
}
