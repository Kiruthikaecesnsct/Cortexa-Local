using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Presentation;

public sealed class AiKeyRowViewModelTests
{
    private readonly InMemorySecretStore _secrets = new();
    private readonly SettingsService _settings;

    public AiKeyRowViewModelTests()
    {
        _settings = new SettingsService(new FakeUserSettingsStore(), _secrets);
    }

    private sealed class StubPassword(string value) : IPasswordSource
    {
        public string GetPassword() => value;

        public void Clear()
        {
        }
    }

    private AiKeyRowViewModel ClaudeRow() => new(
        new AiKeyRowDescriptor
        {
            Slot = SecretSlot.AnthropicApiKey,
            Name = SettingsStrings.ClaudeName,
            Description = SettingsStrings.ClaudeDescription,
            ShortName = "Claude",
            RunsName = "Claude direct runs",
        },
        _settings,
        NullLogger.Instance);

    private AiKeyRowViewModel GitHubRow() => new(
        new AiKeyRowDescriptor
        {
            Slot = SecretSlot.GitHubPat,
            Name = "GitHub",
            Description = "Lists and fetches your GitHub repositories.",
            Hint = "Token scopes.",
            ShortName = "GitHub",
            RunsName = "GitHub fetches",
            Words = CredentialWords.AccessToken,
        },
        _settings,
        NullLogger.Instance);

    private static async Task SaveAsync(AiKeyRowViewModel row, string value)
    {
        row.OpenEditorCommand.Execute(null);
        row.Editor!.PasswordSource = new StubPassword(value);
        await row.Editor.SaveCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task AiRow_KeepsItsOriginalWordingAndStoresInTheProviderSlot()
    {
        var row = ClaudeRow();
        await row.LoadStatusAsync(TestSupport.Ct);

        Assert.Equal("Claude key: Not set", row.ChipAutomationName);
        Assert.Equal("Add key", row.ActionLabel);
        Assert.Equal("Add Claude key", row.ActionAutomationName);
        Assert.False(row.HasHint);

        await SaveAsync(row, "sk-claude");

        Assert.Equal("sk-claude", _secrets.Values[SecretSlot.AnthropicApiKey]);
        Assert.Equal("Claude key saved.", row.Message);
        Assert.Equal("Replace Claude key", row.ActionAutomationName);
    }

    [Fact]
    public void AiRow_ConfirmClear_UsesTheKeyWording()
    {
        var row = ClaudeRow();

        Assert.Equal(
            "Remove the Claude key from this computer? Claude direct runs won't work until you add a key again.",
            row.ConfirmText);
        Assert.Equal("Keep key", row.KeepLabel);
        Assert.Equal("Clear key", row.ConfirmLabel);
    }

    [Fact]
    public async Task TokenRow_UsesTokenWordingHintAndTheRepositorySlot()
    {
        var row = GitHubRow();
        await row.LoadStatusAsync(TestSupport.Ct);

        Assert.True(row.HasHint);
        Assert.Equal("GitHub token: Not set", row.ChipAutomationName);
        Assert.Equal("Add token", row.ActionLabel);
        Assert.Equal("Add GitHub token", row.ActionAutomationName);

        await SaveAsync(row, "ghp_abc");

        Assert.Equal("ghp_abc", _secrets.Values[SecretSlot.GitHubPat]);
        Assert.Equal("GitHub token saved.", row.Message);
        Assert.Equal("Replace", row.ActionLabel);
        Assert.Equal("Keep token", row.KeepLabel);
    }

    [Fact]
    public void TokenRow_EditorCarriesTokenLabels()
    {
        var row = GitHubRow();

        row.OpenEditorCommand.Execute(null);

        Assert.Equal("New GitHub personal access token", row.Editor!.Labels.Label);
        Assert.Equal("Save GitHub token", row.Editor.Labels.SaveName);
        Assert.Equal("Cancel GitHub token entry", row.Editor.Labels.CancelName);
        Assert.Equal(SettingsStrings.TokenEditorHelper, row.Editor.Labels.Helper);
        Assert.Equal("Save token", row.Editor.SaveLabel);
    }

    [Theory]
    [InlineData("", "Paste the token first.")]
    [InlineData("has space", "The value must not contain spaces.")]
    public async Task TokenRow_InvalidToken_ShowsAnErrorAndStoresNothing(string value, string expected)
    {
        var row = GitHubRow();

        await SaveAsync(row, value);

        Assert.Equal(expected, row.Editor!.Error);
        Assert.Empty(_secrets.Values);
    }

    [Fact]
    public async Task TokenRow_Clear_RemovesTheStoredToken()
    {
        _secrets.Values[SecretSlot.GitHubPat] = "ghp_abc";
        var row = GitHubRow();
        await row.LoadStatusAsync(TestSupport.Ct);

        row.BeginClearCommand.Execute(null);
        Assert.Equal("Remove the GitHub token from this computer? GitHub fetches won't work until you add a token again.", row.ConfirmText);
        await row.ConfirmClearCommand.ExecuteAsync(null);

        Assert.Empty(_secrets.Values);
        Assert.Equal("GitHub token cleared.", row.Message);
        Assert.Equal(KeyChipKind.NotSet, row.ChipKind);
    }

    [Fact]
    public async Task FocusPrimary_WhileChecking_WaitsForTheStatusThenRequestsFocus()
    {
        var row = GitHubRow();

        row.FocusPrimary();
        Assert.Null(row.PendingFocus);
        await row.LoadStatusAsync(TestSupport.Ct);

        Assert.Equal(KeyRowFocusKeys.Primary, row.PendingFocus);
    }

    [Fact]
    public async Task FocusPrimary_StatusKnown_RequestsFocusImmediately()
    {
        var row = GitHubRow();
        await row.LoadStatusAsync(TestSupport.Ct);

        row.FocusPrimary();

        Assert.Equal(KeyRowFocusKeys.Primary, row.PendingFocus);
    }
}
