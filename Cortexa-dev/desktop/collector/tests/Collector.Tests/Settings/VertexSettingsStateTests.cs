using Collector.Application.Settings;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Settings;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Settings;

public sealed class VertexSettingsStateTests
{
    private const string StaticProjectId = "static-project";
    private const string StaticLocation = "us-central1";
    private const string UserProjectId = "user-project";
    private const string UserLocation = "europe-west4";

    private static VertexSettingsState Create(FakeUserSettingsStore store) =>
        new(store, MsOptions.Create(new VertexProviderOptions
        {
            ProjectId = StaticProjectId,
            Location = StaticLocation,
        }));

    [Fact]
    public void GetEffectiveSettings_NoUserOverride_FallsBackToStaticOptions()
    {
        var state = Create(new FakeUserSettingsStore());

        var effective = state.GetEffectiveSettings();

        Assert.Equal(new VertexEffectiveSettings(StaticProjectId, StaticLocation), effective);
    }

    [Fact]
    public void GetEffectiveSettings_UserOverridePresentAtConstruction_UsesUserValues()
    {
        var store = new FakeUserSettingsStore();
        store.SeedVertexUserSettings(new VertexUserSettings(UserProjectId, UserLocation));
        var state = Create(store);

        var effective = state.GetEffectiveSettings();

        Assert.Equal(new VertexEffectiveSettings(UserProjectId, UserLocation), effective);
    }

    [Fact]
    public void GetEffectiveSettings_AfterUpdate_ReflectsUpdatedValuesImmediately()
    {
        var state = Create(new FakeUserSettingsStore());

        state.Update(new VertexUserSettings(UserProjectId, UserLocation));
        var effective = state.GetEffectiveSettings();

        Assert.Equal(new VertexEffectiveSettings(UserProjectId, UserLocation), effective);
    }

    [Fact]
    public void GetEffectiveSettings_UpdateWithEmptyProjectId_FallsBackToStaticProjectIdOnly()
    {
        var state = Create(new FakeUserSettingsStore());

        state.Update(new VertexUserSettings(string.Empty, UserLocation));
        var effective = state.GetEffectiveSettings();

        Assert.Equal(new VertexEffectiveSettings(StaticProjectId, UserLocation), effective);
    }

    [Fact]
    public void GetEffectiveSettings_UpdateWithEmptyLocation_FallsBackToStaticLocationOnly()
    {
        var state = Create(new FakeUserSettingsStore());

        state.Update(new VertexUserSettings(UserProjectId, string.Empty));
        var effective = state.GetEffectiveSettings();

        Assert.Equal(new VertexEffectiveSettings(UserProjectId, StaticLocation), effective);
    }

    [Fact]
    public void GetEffectiveSettings_UpdateWithWhitespaceValues_TreatedAsNotSet()
    {
        var state = Create(new FakeUserSettingsStore());

        state.Update(new VertexUserSettings("   ", "   "));
        var effective = state.GetEffectiveSettings();

        Assert.Equal(new VertexEffectiveSettings(StaticProjectId, StaticLocation), effective);
    }
}
