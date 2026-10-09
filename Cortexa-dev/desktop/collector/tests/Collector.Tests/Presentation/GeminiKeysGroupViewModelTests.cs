using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Presentation;

public sealed class GeminiKeysGroupViewModelTests
{
    private sealed class StubPassword(string value) : IPasswordSource
    {
        public string GetPassword() => value;

        public void Clear()
        {
        }
    }

    private static GeminiKeysGroupViewModel ViewModel(FakeGeminiKeyStore keyStore, FakeGeminiKeyStatusStore? statusStore = null)
    {
        var settings = new SettingsService(new FakeUserSettingsStore(), new InMemorySecretStore(), keyStore);
        return new GeminiKeysGroupViewModel(settings, statusStore ?? new FakeGeminiKeyStatusStore(), NullLogger.Instance);
    }

    [Fact]
    public async Task SaveAsync_KeyMatchesAnExistingEntry_IsRejectedAsDuplicateWithoutCallingAddKey()
    {
        const string DuplicateValue = "duplicate-key-value";
        var keyStore = new FakeGeminiKeyStore();
        await keyStore.AddKeyAsync(DuplicateValue, TestSupport.Ct);
        var vm = ViewModel(keyStore);
        await vm.LoadAsync(TestSupport.Ct);
        vm.OpenEditorCommand.Execute(null);
        vm.Editor!.PasswordSource = new StubPassword(DuplicateValue);

        await vm.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(SettingsStrings.DuplicateKey, vm.Editor!.Error);
        Assert.Equal(1, keyStore.AddKeyCalls);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task SaveAsync_NewKeyValue_AddsItAndClosesTheEditor()
    {
        var keyStore = new FakeGeminiKeyStore();
        var vm = ViewModel(keyStore);
        await vm.LoadAsync(TestSupport.Ct);
        vm.OpenEditorCommand.Execute(null);
        vm.Editor!.PasswordSource = new StubPassword("brand-new-key-value");

        await vm.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, keyStore.AddKeyCalls);
        Assert.Single(vm.Items);
        Assert.Null(vm.Editor);
    }

    [Fact]
    public async Task MoveDown_ReorderPersistFails_RevertsTheCollectionToItsPreviousOrder()
    {
        var keyStore = new FakeGeminiKeyStore();
        var first = await keyStore.AddKeyAsync("first-key-value", TestSupport.Ct);
        var second = await keyStore.AddKeyAsync("second-key-value", TestSupport.Ct);
        var vm = ViewModel(keyStore);
        await vm.LoadAsync(TestSupport.Ct);
        keyStore.ReorderFailure = new SecretStoreException("reorder rejected");
        var originalOrder = vm.Items.Select(item => item.Id).ToArray();

        vm.MoveDown(vm.Items[0]);
        await Task.Yield();

        Assert.Equal(originalOrder, vm.Items.Select(item => item.Id));
        Assert.Equal([first.Id, second.Id], originalOrder);
    }

    [Fact]
    public async Task MoveDown_ReorderPersistSucceeds_KeepsTheNewOrder()
    {
        var keyStore = new FakeGeminiKeyStore();
        var first = await keyStore.AddKeyAsync("first-key-value", TestSupport.Ct);
        var second = await keyStore.AddKeyAsync("second-key-value", TestSupport.Ct);
        var vm = ViewModel(keyStore);
        await vm.LoadAsync(TestSupport.Ct);

        vm.MoveDown(vm.Items[0]);
        await Task.Yield();

        Assert.Equal([second.Id, first.Id], vm.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task ConfirmRemoveAsync_RemovedKey_UpdatesTheList()
    {
        var keyStore = new FakeGeminiKeyStore();
        await keyStore.AddKeyAsync("key-to-remove", TestSupport.Ct);
        var vm = ViewModel(keyStore);
        await vm.LoadAsync(TestSupport.Ct);
        var item = Assert.Single(vm.Items);

        await vm.ConfirmRemoveAsync(item, TestSupport.Ct);

        Assert.Empty(vm.Items);
        Assert.Equal(1, keyStore.RemoveKeyCalls);
    }
}
