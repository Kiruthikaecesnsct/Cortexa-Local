namespace Collector.Application.Remote.Selection;

public readonly record struct FileTreeCounts(
    int Files,
    int Supported,
    int Unsupported,
    int TooLarge,
    long SupportedBytes,
    int UnknownSize = 0)
{
    public static FileTreeCounts Zero => default;

    public static FileTreeCounts ForFile(RemoteEntryVerdict verdict, long? sizeBytes) => verdict switch
    {
        RemoteEntryVerdict.Supported => new FileTreeCounts(1, 1, 0, 0, sizeBytes ?? 0, sizeBytes is null ? 1 : 0),
        RemoteEntryVerdict.TooLarge => new FileTreeCounts(1, 0, 0, 1, 0),
        _ => new FileTreeCounts(1, 0, 1, 0, 0),
    };

    public static FileTreeCounts operator +(FileTreeCounts left, FileTreeCounts right) => new(
        left.Files + right.Files,
        left.Supported + right.Supported,
        left.Unsupported + right.Unsupported,
        left.TooLarge + right.TooLarge,
        left.SupportedBytes + right.SupportedBytes,
        left.UnknownSize + right.UnknownSize);

    public static FileTreeCounts operator -(FileTreeCounts left, FileTreeCounts right) => new(
        left.Files - right.Files,
        left.Supported - right.Supported,
        left.Unsupported - right.Unsupported,
        left.TooLarge - right.TooLarge,
        left.SupportedBytes - right.SupportedBytes,
        left.UnknownSize - right.UnknownSize);
}
