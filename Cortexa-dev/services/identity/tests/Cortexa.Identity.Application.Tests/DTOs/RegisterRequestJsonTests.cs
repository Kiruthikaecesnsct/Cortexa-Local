using System.Text.Json;
using Cortexa.Identity.Application.DTOs;
using Xunit;

namespace Cortexa.Identity.Application.Tests.DTOs;

public sealed class RegisterRequestJsonTests
{
    [Fact]
    public void Deserialize_FrontendSnakeCasePayload_BindsDisplayName()
    {
        const string payload = """{"email":"new@cortexa.io","password":"S3cur3P@ssw0rd!","display_name":"New User"}""";

        var request = JsonSerializer.Deserialize<RegisterRequest>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal("new@cortexa.io", request!.Email);
        Assert.Equal("New User", request.DisplayName);
    }
}
