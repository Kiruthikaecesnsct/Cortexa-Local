using System.Security.Cryptography;
using System.Text;

namespace Collector.Infrastructure.Remote.Ssh;

internal static class SshFingerprint
{
    private const string Prefix = "SHA256:";
    private const char Base64Padding = '=';

    public static string Compute(byte[] hostKey)
    {
        var hash = SHA256.HashData(hostKey);
        return Prefix + Convert.ToBase64String(hash).TrimEnd(Base64Padding);
    }

    public static string Normalize(string fingerprint)
    {
        var trimmed = fingerprint.Trim();
        var body = trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? trimmed[Prefix.Length..] : trimmed;
        return Prefix + body.Trim().TrimEnd(Base64Padding);
    }

    public static bool Matches(string pinnedFingerprint, string candidateFingerprint)
    {
        var pinnedBytes = Encoding.UTF8.GetBytes(Normalize(pinnedFingerprint));
        var candidateBytes = Encoding.UTF8.GetBytes(Normalize(candidateFingerprint));
        return pinnedBytes.Length == candidateBytes.Length
            && CryptographicOperations.FixedTimeEquals(pinnedBytes, candidateBytes);
    }

    public static bool IsTrusted(string? pinnedFingerprint, byte[] hostKey) =>
        !string.IsNullOrWhiteSpace(pinnedFingerprint) && Matches(pinnedFingerprint, Compute(hostKey));
}
