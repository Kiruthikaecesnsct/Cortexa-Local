using Collector.Server.Api.Auth;
using Collector.Server.Api.Endpoints;
using Collector.Server.Api.Json;
using Collector.Server.Application;
using Collector.Server.Infrastructure;
using Collector.Server.Infrastructure.Health;
using Collector.Server.Infrastructure.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCollectorApplication(builder.Configuration);
builder.Services.AddCollectorInfrastructure(builder.Configuration);
builder.Services.AddCollectorHttpJson();
builder.Services.AddCollectorAuthentication();

var app = builder.Build();

LogSigningKeyFingerprint(app);

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(HealthTags.Ready) });
app.MapKnowledgeUploadEndpoints();
app.MapCollectorBatchesEndpoints();

app.Run();

static void LogSigningKeyFingerprint(WebApplication app)
{
    var identity = app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
    var fingerprint = SigningKeyFingerprint.Compute(identity.SigningKey);
    app.Logger.LogInformation(
        "Collector Server identity signing-key fingerprint: {SigningKeyFingerprint}. " +
        "Compare against the fingerprint logged by the native identity/api-gateway run to " +
        "confirm the keys are in sync (US146).",
        fingerprint);
}

public partial class Program;
