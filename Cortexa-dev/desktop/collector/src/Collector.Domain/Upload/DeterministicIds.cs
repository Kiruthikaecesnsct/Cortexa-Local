using System.Security.Cryptography;
using System.Text;

namespace Collector.Domain.Upload;

public static class DeterministicIds
{
    private const int GuidByteCount = 16;
    private const int VersionByteIndex = 6;
    private const int VariantByteIndex = 8;
    private const byte VersionFiveBits = 0x50;
    private const byte VersionMask = 0x0F;
    private const byte VariantBits = 0x80;
    private const byte VariantMask = 0x3F;

    public static readonly Guid Namespace = new("6f1d2c4e-8a37-5b90-9d41-3c7e5a2b8f10");

    public static string BatchId(string userId, string idempotencyKey) =>
        Create(Namespace, $"{userId}:{idempotencyKey}").ToString();

    public static string DocumentId(string batchId, Guid clientDocumentId) =>
        Create(Namespace, $"{batchId}:{clientDocumentId:D}").ToString();

    public static Guid Create(Guid namespaceId, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[GuidByteCount + nameBytes.Length];
        namespaceId.TryWriteBytes(input, bigEndian: true, out _);
        nameBytes.CopyTo(input, GuidByteCount);

        var hash = SHA1.HashData(input);
        var bytes = hash.AsSpan(0, GuidByteCount).ToArray();
        bytes[VersionByteIndex] = (byte)((bytes[VersionByteIndex] & VersionMask) | VersionFiveBits);
        bytes[VariantByteIndex] = (byte)((bytes[VariantByteIndex] & VariantMask) | VariantBits);
        return new Guid(bytes, bigEndian: true);
    }
}
