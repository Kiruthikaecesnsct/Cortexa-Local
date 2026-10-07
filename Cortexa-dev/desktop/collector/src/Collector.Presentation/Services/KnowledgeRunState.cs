using Collector.Application.Knowledge;

namespace Collector.Presentation.Services;

public sealed class KnowledgeRunState
{
    public ExtractionRunResult? Current { get; private set; }

    public event EventHandler? Changed;

    public void Set(ExtractionRunResult result)
    {
        Current = result;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
