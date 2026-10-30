using Collector.Application.Settings;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Presentation;

public sealed class VertexRowViewModelTests
{
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeVertexCredentialStore _credentials = new();
    private readonly FakeUserSettingsStore _store = new();
    private readonly FakeVertexSettingsState _state = new();
    private readonly FakeFilePicker _filePicker = new([]);

    private VertexRowViewModel Row() => new(_credentials, _secrets, _store, _state, _filePicker, NullLogger.Instance);

    private static async Task SaveServiceAccountAsync(VertexRowViewModel row, string json)
    {
        row.OpenEditorCommand.Execute(null);
        row.Editor!.PasteText = json;
        await row.Editor.SaveCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task SaveCommand_ProjectIdEmpty_ShowsInlineErrorAndDoesNotSave()
    {
        var row = Row();
        await SaveServiceAccountAsync(row, "{\"type\":\"service_account\"}");
        row.Location = "us-central1";
        row.ProjectId = string.Empty;

        await row.SaveCommand.ExecuteAsync(null);

        Assert.Equal(SettingsStrings.VertexProjectIdEmpty, row.ProjectIdError);
        Assert.Equal(0, _store.SaveCalls);
        Assert.Equal(0, _state.UpdateCalls);
    }

    [Fact]
    public async Task SaveCommand_LocationEmpty_ShowsInlineErrorAndDoesNotSave()
    {
        var row = Row();
        await SaveServiceAccountAsync(row, "{\"type\":\"service_account\"}");
        row.ProjectId = "cortexa-project";
        row.Location = string.Empty;

        await row.SaveCommand.ExecuteAsync(null);

        Assert.Equal(SettingsStrings.VertexLocationEmpty, row.LocationError);
        Assert.Equal(0, _state.UpdateCalls);
    }

    [Fact]
    public async Task SaveCommand_ServiceAccountNotSaved_ShowsInlineErrorAndDoesNotSave()
    {
        var row = Row();
        await row.LoadStatusAsync(TestContext.Current.CancellationToken);
        row.ProjectId = "cortexa-project";
        row.Location = "us-central1";

        await row.SaveCommand.ExecuteAsync(null);

        Assert.True(row.HasError);
        Assert.Equal(SettingsStrings.VertexServiceAccountMissing, row.ErrorMessage);
        Assert.Equal(0, _state.UpdateCalls);
    }

    [Fact]
    public async Task SaveCommand_AllThreePresent_SavesAndUpdatesVertexSettingsState()
    {
        var row = Row();
        await SaveServiceAccountAsync(row, "{\"type\":\"service_account\"}");
        row.ProjectId = "cortexa-project";
        row.Location = "us-central1";

        await row.SaveCommand.ExecuteAsync(null);

        Assert.False(row.HasError);
        Assert.Null(row.ProjectIdError);
        Assert.Null(row.LocationError);
        Assert.Equal(1, _state.UpdateCalls);
        Assert.Equal(new VertexUserSettings("cortexa-project", "us-central1"), _store.VertexUser);
        Assert.False(row.IsDirty);
    }

    [Fact]
    public async Task LoadStatusAsync_NoServiceAccount_ShowsNotSavedChip()
    {
        var row = Row();

        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(VertexSaChipKind.NotSaved, row.ChipKind);
        Assert.False(row.HasSa);
    }

    [Fact]
    public async Task LoadStatusAsync_ServiceAccountPresent_ShowsSavedChip()
    {
        _credentials.ServiceAccountJson = "{\"type\":\"service_account\"}";
        var row = Row();

        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(VertexSaChipKind.Saved, row.ChipKind);
        Assert.True(row.HasSa);
    }

    [Fact]
    public async Task ConfirmClearCommand_Success_ReturnsToNotSaved()
    {
        await _secrets.WriteAsync(Collector.Application.Secrets.SecretSlot.VertexServiceAccount, "{}", TestContext.Current.CancellationToken);
        _credentials.ServiceAccountJson = "{}";
        var row = Row();
        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        row.BeginClearCommand.Execute(null);
        await row.ConfirmClearCommand.ExecuteAsync(null);

        Assert.Equal(VertexSaChipKind.NotSaved, row.ChipKind);
        Assert.False(row.IsConfirmingClear);
        Assert.True(row.HasMessage);
    }
}
