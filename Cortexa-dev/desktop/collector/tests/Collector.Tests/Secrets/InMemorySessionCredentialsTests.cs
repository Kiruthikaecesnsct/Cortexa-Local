using Collector.Domain.Enums;
using Collector.Infrastructure.Secrets;

namespace Collector.Tests.Secrets;

public sealed class InMemorySessionCredentialsTests
{
    private readonly InMemorySessionCredentials _credentials = new();

    [Fact]
    public void Tokens_AreKeptPerProvider()
    {
        _credentials.SetToken(SourceType.Github, "gh");
        _credentials.SetToken(SourceType.AzureDevops, "ado");

        Assert.Equal("gh", _credentials.GetToken(SourceType.Github));
        Assert.Equal("ado", _credentials.GetToken(SourceType.AzureDevops));
    }

    [Fact]
    public void Clear_RemovesOnlyThatProvidersToken()
    {
        _credentials.SetToken(SourceType.Github, "gh");
        _credentials.SetToken(SourceType.AzureDevops, "ado");

        _credentials.Clear(SourceType.Github);

        Assert.Null(_credentials.GetToken(SourceType.Github));
        Assert.Equal("ado", _credentials.GetToken(SourceType.AzureDevops));
    }

    [Fact]
    public void Clear_Ssh_RemovesThePassphrase()
    {
        _credentials.SetSshPassphrase("phrase");

        _credentials.Clear(SourceType.Ssh);

        Assert.Null(_credentials.GetSshPassphrase());
    }

    [Fact]
    public void Clear_OtherProvider_KeepsThePassphrase()
    {
        _credentials.SetSshPassphrase("phrase");

        _credentials.Clear(SourceType.Github);

        Assert.Equal("phrase", _credentials.GetSshPassphrase());
    }

    [Fact]
    public void ClearAll_RemovesEveryCredential()
    {
        _credentials.SetToken(SourceType.Github, "gh");
        _credentials.SetToken(SourceType.AzureDevops, "ado");
        _credentials.SetSshPassphrase("phrase");

        _credentials.ClearAll();

        Assert.Null(_credentials.GetToken(SourceType.Github));
        Assert.Null(_credentials.GetToken(SourceType.AzureDevops));
        Assert.Null(_credentials.GetSshPassphrase());
    }

    [Fact]
    public void GetToken_NeverSet_ReturnsNull() => Assert.Null(_credentials.GetToken(SourceType.Github));
}
