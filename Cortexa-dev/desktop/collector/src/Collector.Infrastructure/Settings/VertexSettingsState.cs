using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Settings;

public sealed class VertexSettingsState : IVertexSettingsState
{
    private readonly IOptions<VertexProviderOptions> _staticOptions;
    private readonly object _gate = new();
    private VertexUserSettings _current;

    public VertexSettingsState(IUserSettingsStore store, IOptions<VertexProviderOptions> staticOptions)
    {
        _staticOptions = staticOptions;
        _current = store.GetVertexUserSettings();
    }

    public void Update(VertexUserSettings settings)
    {
        lock (_gate)
        {
            _current = settings;
        }
    }

    public VertexEffectiveSettings GetEffectiveSettings()
    {
        VertexUserSettings current;
        lock (_gate)
        {
            current = _current;
        }

        var fallback = _staticOptions.Value;
        var projectId = string.IsNullOrWhiteSpace(current.ProjectId) ? fallback.ProjectId : current.ProjectId;
        var location = string.IsNullOrWhiteSpace(current.Location) ? fallback.Location : current.Location;
        return new VertexEffectiveSettings(projectId, location);
    }
}
