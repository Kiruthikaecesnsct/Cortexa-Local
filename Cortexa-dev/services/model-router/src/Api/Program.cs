using Cortexa.ModelRouter.Api.Endpoints;
using Cortexa.ModelRouter.Infrastructure;
using Sentry;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

builder.WebHost.UseSentry(o =>
{
    o.Dsn = Environment.GetEnvironmentVariable("SENTRY_DSN") ?? string.Empty;
    o.Environment = builder.Configuration["Sentry:Environment"] ?? "dev";
    o.TracesSampleRate = builder.Configuration.GetValue<double?>("Sentry:TracesSampleRate") ?? 0.1;
    o.SendDefaultPii = false;
    o.EnableLogs = true;
    o.SetBeforeSend(sentryEvent =>
        sentryEvent.Exception is OperationCanceledException or TaskCanceledException
            ? null
            : sentryEvent);
});

builder.Services.AddModelRouterInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();

// Metrics are auto-enabled once the SDK is initialised; emit a startup counter.
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTRY_DSN")))
{
    SentrySdk.Metrics.EmitCounter("service.started", 1);
    SentrySdk.Logger.LogInfo("model-router service started");
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapCompleteEndpoints();
app.MapModelsEndpoints();
app.Run();
