using System.Globalization;

namespace Collector.Infrastructure.Remote.Ssh;

internal readonly record struct SshBlobKey(string RelativePath, long MtimeTicks, long SizeBytes);

internal static class SshBlobKeyCodec
{
    private const char FieldDelimiter = '\u0001';
    private const char PairDelimiter = '\u0002';

    public static string Encode(string relativePath, long mtimeTicks, long sizeBytes)
    {
        if (!CanEncode(relativePath))
        {
            throw new ArgumentException("Relative path contains a reserved control character.", nameof(relativePath));
        }

        var mtime = mtimeTicks.ToString(CultureInfo.InvariantCulture);
        var size = sizeBytes.ToString(CultureInfo.InvariantCulture);
        return $"{relativePath}{FieldDelimiter}{mtime}{PairDelimiter}{size}";
    }

    public static bool CanEncode(string relativePath) =>
        relativePath.IndexOf(FieldDelimiter) < 0 && relativePath.IndexOf(PairDelimiter) < 0;

    public static SshBlobKey Parse(string blobKey)
    {
        var fieldParts = blobKey.Split(FieldDelimiter, 2);
        if (fieldParts.Length != 2)
        {
            throw new FormatException("SSH blob key is missing the relative path field.");
        }

        var pairParts = fieldParts[1].Split(PairDelimiter, 2);
        if (pairParts.Length != 2)
        {
            throw new FormatException("SSH blob key is missing the mtime/size field.");
        }

        var mtimeTicks = long.Parse(pairParts[0], CultureInfo.InvariantCulture);
        var sizeBytes = long.Parse(pairParts[1], CultureInfo.InvariantCulture);
        return new SshBlobKey(fieldParts[0], mtimeTicks, sizeBytes);
    }
}
