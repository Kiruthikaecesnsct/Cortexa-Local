using Collector.Application.Settings;
using Collector.Infrastructure.Remote.Ssh;

namespace Collector.Tests.Remote.Ssh;

public sealed class SftpRootPathTests
{
    [Theory]
    [InlineData("~", ".")]
    [InlineData("~/", ".")]
    [InlineData("~//", ".")]
    [InlineData("~/x", "x")]
    [InlineData("~/x/", "x")]
    [InlineData("~/x/y", "x/y")]
    [InlineData("/var/data", "/var/data")]
    [InlineData("/", "/")]
    public void Resolve_MapsHomeToTheCurrentDirectory(string root, string expected) =>
        Assert.Equal(expected, SftpRootPath.Resolve(root));

    private static readonly SshConnectionProfile Profile = new("host", 22, "alice", "/keys/a", "/data");

    [Fact]
    public void IsSameConnection_Identical_ReturnsTrue() =>
        Assert.True(SshProfileIdentity.IsSameConnection(Profile with { Host = "HOST" }, Profile));

    [Fact]
    public void IsSameConnection_KeyFileChanged_ReturnsFalse() =>
        Assert.False(SshProfileIdentity.IsSameConnection(Profile, Profile with { KeyFilePath = "/keys/b" }));

    [Theory]
    [InlineData("port")]
    [InlineData("user")]
    [InlineData("root")]
    public void IsSameConnection_OtherFieldChanged_ReturnsFalse(string field)
    {
        var changed = field switch
        {
            "port" => Profile with { Port = 2222 },
            "user" => Profile with { Username = "bob" },
            _ => Profile with { RemoteRoot = "/other" },
        };

        Assert.False(SshProfileIdentity.IsSameConnection(Profile, changed));
    }

    [Fact]
    public void IsSameConnection_NoPreviousProfile_ReturnsFalse() =>
        Assert.False(SshProfileIdentity.IsSameConnection(null, Profile));
}
