namespace Collector.Application.Ports;

public interface ITokenCounter
{
    IReadOnlyList<int> Encode(string text);

    string Decode(IReadOnlyList<int> tokens);

    int Count(string text);
}
