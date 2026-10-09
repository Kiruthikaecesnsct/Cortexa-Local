namespace Collector.Application.Secrets;

public sealed record GeminiKey(string Id, string Key)
{
    public string Last4 => Last4Of(Key);

    public static string Last4Of(string key) => key.Length <= 4 ? key : key[^4..];
}

public sealed record GeminiKeySummary(string Id, string Last4);

public sealed record GeminiKeyList(IReadOnlyList<GeminiKey> Keys)
{
    public static readonly GeminiKeyList Empty = new([]);

    public IReadOnlyList<GeminiKeySummary> ToSummaries() =>
        [.. Keys.Select(key => new GeminiKeySummary(key.Id, key.Last4))];
}
