using System.Security.Cryptography;
using System.Text;

namespace Collector.Server.Infrastructure.Identity;

public static class SigningKeyFingerprint
{
    private const int FingerprintLength = 12;

    public static string Compute(string signingKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signingKey));
        return Convert.ToHexString(hash)[..FingerprintLength];
    }
}
