namespace Collector.Application.Ports;

public interface IGeminiModelExhaustion
{
    bool IsExhausted(string model);

    void MarkExhausted(string model);

    void Reset();
}
