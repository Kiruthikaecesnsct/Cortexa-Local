namespace Collector.Presentation.Resources;

public sealed record CredentialWords
{
    public static CredentialWords ApiKey { get; } = new()
    {
        Noun = "key",
        EditorNoun = "API key",
        AddLabel = SettingsStrings.AddKey,
        SaveLabel = SettingsStrings.SaveKey,
        ClearLabel = SettingsStrings.ClearKey,
        KeepLabel = SettingsStrings.KeepKey,
        EditorHelper = SettingsStrings.EditorHelper,
        PasteFirst = SettingsStrings.PasteFirst,
        TooLong = SettingsStrings.KeyTooLong,
        SaveFailed = SettingsStrings.KeySaveFailed,
        ClearFailed = SettingsStrings.KeyClearFailed,
        StatusUnknown = SettingsStrings.StatusUnknownHelper,
    };

    public static CredentialWords AccessToken { get; } = new()
    {
        Noun = "token",
        EditorNoun = "personal access token",
        AddLabel = SettingsStrings.AddToken,
        SaveLabel = SettingsStrings.SaveToken,
        ClearLabel = SettingsStrings.ClearToken,
        KeepLabel = SettingsStrings.KeepToken,
        EditorHelper = SettingsStrings.TokenEditorHelper,
        PasteFirst = SettingsStrings.PasteTokenFirst,
        TooLong = SettingsStrings.TokenTooLong,
        SaveFailed = SettingsStrings.TokenSaveFailed,
        ClearFailed = SettingsStrings.TokenClearFailed,
        StatusUnknown = SettingsStrings.TokenStatusUnknownHelper,
    };

    public required string Noun { get; init; }

    public required string EditorNoun { get; init; }

    public required string AddLabel { get; init; }

    public required string SaveLabel { get; init; }

    public required string ClearLabel { get; init; }

    public required string KeepLabel { get; init; }

    public required string EditorHelper { get; init; }

    public required string PasteFirst { get; init; }

    public required string TooLong { get; init; }

    public required string SaveFailed { get; init; }

    public required string ClearFailed { get; init; }

    public required string StatusUnknown { get; init; }
}
