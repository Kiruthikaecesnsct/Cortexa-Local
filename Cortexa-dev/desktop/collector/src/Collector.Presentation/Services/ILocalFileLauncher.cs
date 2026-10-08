using Collector.Application.History;

namespace Collector.Presentation.Services;

public interface ILocalFileLauncher
{
    bool TryLaunch(LocalSourceTarget target);
}
