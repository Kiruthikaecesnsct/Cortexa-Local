using Collector.Infrastructure.Remote.Ssh;

namespace Collector.Tests.Remote.Ssh;

public class SshFingerprintTests
{
    // Public key blob and fingerprint of a throwaway ed25519 key, as printed by `ssh-keygen -lf`.
    private const string PublicKeyBlob = "AAAAC3NzaC1lZDI1NTE5AAAAIP5pzr/cjFGbmicJij8JmDgtv+M3rA4B6qZLCzlR4WQf";
    private const string KeygenFingerprint = "SHA256:OmRN5FdZhzdzZ7N3Mq3IJqCzm8tBJhJ8mQhsHyf8Uh8";

    private static byte[] HostKey => Convert.FromBase64String(PublicKeyBlob);

    [Fact]
    public void Compute_MatchesSshKeygenOutput()
    {
        Assert.Equal(KeygenFingerprint, SshFingerprint.Compute(HostKey));
    }

    [Theory]
    [InlineData(KeygenFingerprint)]
    [InlineData("  " + KeygenFingerprint + "  ")]
    [InlineData(KeygenFingerprint + "=")]
    [InlineData("sha256:OmRN5FdZhzdzZ7N3Mq3IJqCzm8tBJhJ8mQhsHyf8Uh8")]
    [InlineData("OmRN5FdZhzdzZ7N3Mq3IJqCzm8tBJhJ8mQhsHyf8Uh8")]
    public void IsTrusted_PastedFingerprintVariants_AreAccepted(string pinned)
    {
        Assert.True(SshFingerprint.IsTrusted(pinned, HostKey));
    }

    [Theory]
    [InlineData("SHA256:OmRN5FdZhzdzZ7N3Mq3IJqCzm8tBJhJ8mQhsHyf8Uh9")]
    [InlineData("SHA256:omrn5fdzhzdzz7n3mq3ijqczm8tbjhj8mqhshyf8uh8")]
    [InlineData("SHA256:")]
    public void IsTrusted_DifferentFingerprint_IsRejected(string pinned)
    {
        Assert.False(SshFingerprint.IsTrusted(pinned, HostKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTrusted_NoPinnedFingerprint_IsRejected(string? pinned)
    {
        Assert.False(SshFingerprint.IsTrusted(pinned, HostKey));
    }

    [Fact]
    public void Matches_DifferentLengths_ReturnsFalse()
    {
        Assert.False(SshFingerprint.Matches(KeygenFingerprint, KeygenFingerprint + "A"));
    }
}
