using Collector.Infrastructure.Extraction;
using Microsoft.ML.Tokenizers;

namespace Collector.Tests.Extraction;

public sealed class MlTokenizerCounterTests
{
    private readonly MlTokenizerCounter _counter = new();

    [Fact]
    public void Count_matches_a_manual_cl100k_base_tokenizer_run()
    {
        const string text = "Text tokenization is the process of splitting a string into a list of tokens.";
        var reference = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        var expected = reference.CountTokens(text);

        var actual = _counter.Count(text);

        Assert.Equal(expected, actual);
        Assert.True(actual > 0);
    }

    [Fact]
    public void Encode_then_decode_round_trips_the_original_text()
    {
        const string text = "Round trip tokenization should preserve the source text exactly.";

        var ids = _counter.Encode(text);
        var decoded = _counter.Decode(ids);

        Assert.Equal(text, decoded);
    }

    [Fact]
    public void Count_equals_the_number_of_encoded_ids()
    {
        const string text = "Cortexa reads raw research material and finds patentable inventions.";

        var ids = _counter.Encode(text);

        Assert.Equal(ids.Count, _counter.Count(text));
    }
}
