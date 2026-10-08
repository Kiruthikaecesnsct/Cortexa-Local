using System.Security.Cryptography;
using System.Text;

namespace Collector.Infrastructure.Remote.Ssh;

internal static class SshFingerprint
{
    private const string Prefix = "SHA256:";

    public static string Compute(byte[] hostKey)
    {
        var hash = SHA256.HashData(hostKey);
        return Prefix + Convert.ToBase64String(hash).TrimEnd('=');
    }

    public static bool Matches(string pinnedFingerprint, string candidateFingerprint)
    {
        var pinnedBytes = Encoding.UTF8.GetBytes(pinnedFingerprint);
        var candidateBytes = Encoding.UTF8.GetBytes(candidateFingerprint);
        return pinnedBytes.Length == candidateBytes.Length
            && CryptographicOperations.FixedTimeEquals(pinnedBytes, candidateBytes);
    }
}
