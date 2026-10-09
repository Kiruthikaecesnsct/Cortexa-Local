namespace Collector.Infrastructure.Remote.Cortexa;

internal readonly record struct CortexaBlobKey(string Branch, string Version, string RelativePath);

internal static class CortexaBlobKeyCodec
{
    private const char FieldDelimiter = '\u0001';
    private const char PairDelimiter = '\u0002';

    public static string Encode(string branch, string version, string relativePath)
    {
        if (!CanEncode(branch) || !CanEncode(version) || !CanEncode(relativePath))
        {
            throw new ArgumentException("Blob key field contains a reserved control character.", nameof(relativePath));
        }

        return $"{branch}{FieldDelimiter}{version}{PairDelimiter}{relativePath}";
    }

    public static bool CanEncode(string value) =>
        value.IndexOf(FieldDelimiter) < 0 && value.IndexOf(PairDelimiter) < 0;

    public static CortexaBlobKey Parse(string blobKey)
    {
        var fieldParts = blobKey.Split(FieldDelimiter, 2);
        if (fieldParts.Length != 2)
        {
            throw new FormatException("Cortexa blob key is missing the branch field.");
        }

        var pairParts = fieldParts[1].Split(PairDelimiter, 2);
        if (pairParts.Length != 2)
        {
            throw new FormatException("Cortexa blob key is missing the version/path field.");
        }

        return new CortexaBlobKey(fieldParts[0], pairParts[0], pairParts[1]);
    }
}
