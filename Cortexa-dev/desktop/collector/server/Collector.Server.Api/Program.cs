using Collector.Server.Api.Auth;
using Collector.Server.Api.Endpoints;
using Collector.Server.Api.Json;
using Collector.Server.Application;
using Collector.Server.Infrastructure;
using Collector.Server.Infrastructure.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCollectorApplication(builder.Configuration);
builder.Services.AddCollectorInfrastructure(builder.Configuration);
builder.Services.AddCollectorHttpJson();
builder.Services.AddCollectorAuthentication();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(HealthTags.Ready) });
app.MapKnowledgeUploadEndpoints();
app.MapCollectorBatchesEndpoints();

app.Run();

public partial class Program;
