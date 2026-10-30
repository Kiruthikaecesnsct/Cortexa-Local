using Collector.Application.Settings;

namespace Collector.Application.Ports;

public interface IVertexSettingsState
{
    VertexEffectiveSettings GetEffectiveSettings();

    void Update(VertexUserSettings settings);
}
