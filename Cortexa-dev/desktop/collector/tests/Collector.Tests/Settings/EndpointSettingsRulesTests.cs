using Collector.Application.Settings;

namespace Collector.Tests.Settings;

public class EndpointSettingsRulesTests
{
    [Theory]
    [InlineData("https://gateway.example")]
    [InlineData("https://gateway.example:8443/api")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("HTTP://LOCALHOST:8080")]
    public void Accepts_allowed_urls(string url)
    {
        Assert.Null(EndpointSettingsRules.ValidateUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gateway.example")]
    [InlineData("/relative/path")]
    [InlineData("http://gateway.example")]
    [InlineData("ftp://localhost")]
    [InlineData("http://localhost.evil.example")]
    public void Rejects_invalid_urls(string? url)
    {
        Assert.NotNull(EndpointSettingsRules.ValidateUrl(url));
    }

    [Fact]
    public void Reports_the_first_invalid_field()
    {
        var gateway = EndpointSettingsRules.Validate(new EndpointSettings("nope", "https://server.example"));
        var server = EndpointSettingsRules.Validate(new EndpointSettings("https://gateway.example", "nope"));
        var ok = EndpointSettingsRules.Validate(new EndpointSettings("https://gateway.example", "https://server.example"));

        Assert.Equal(EndpointField.GatewayUrl, gateway.Field);
        Assert.Equal(EndpointField.CollectorServerUrl, server.Field);
        Assert.True(ok.IsValid);
    }
}
