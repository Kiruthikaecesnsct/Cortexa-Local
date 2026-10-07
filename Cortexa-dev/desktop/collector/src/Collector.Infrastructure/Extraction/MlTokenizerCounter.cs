using Collector.Application.Ports;
using Microsoft.ML.Tokenizers;

namespace Collector.Infrastructure.Extraction;

public sealed class MlTokenizerCounter : ITokenCounter
{
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    public IReadOnlyList<int> Encode(string text) => _tokenizer.EncodeToIds(text);

    public string Decode(IReadOnlyList<int> tokens) => _tokenizer.Decode(tokens);

    public int Count(string text) => _tokenizer.CountTokens(text);
}
