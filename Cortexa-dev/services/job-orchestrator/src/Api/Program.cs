using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Cortexa.JobOrchestrator.Api.Auth;
using Cortexa.JobOrchestrator.Api.Endpoints;
using Cortexa.JobOrchestrator.Api.Middleware;
using Cortexa.JobOrchestrator.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
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
});

var vaultUri = builder.Configuration["KeyVault:Uri"] ?? string.Empty;

if (!string.IsNullOrWhiteSpace(vaultUri))
    await LoadKeyVaultSecretsAsync(builder.Configuration, vaultUri);

builder.Services.AddJobOrchestratorInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

builder.Services.AddOrchestratorAuthentication(builder.Configuration);

builder.Services.Configure<FormOptions>(options =>
{
    var maxFileMb = builder.Configuration.GetValue<long>("Multipart:MaxFileSizeMb", 100);
    options.MultipartBodyLengthLimit = maxFileMb * 1024 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

// Metrics are auto-enabled once the SDK is initialised; emit a startup counter.
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTRY_DSN")))
{
    SentrySdk.Metrics.EmitCounter("service.started", 1);
    SentrySdk.Logger.LogInfo("job-orchestrator service started");
}

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapSagaStatusEndpoints();
app.MapBatchLifecycleEndpoints();
app.MapReconciliationEndpoints();
app.MapConfigEndpoints();
app.MapPatentConfigEndpoints();
app.MapPatentSecretsEndpoints();
app.MapDocumentContentEndpoints();

app.Run();

static async Task LoadKeyVaultSecretsAsync(IConfiguration configuration, string vaultUri)
{
    var client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());

    await TryLoadSecretAsync(client, configuration, "KeyVault:CosmosUriSecretName", "Cosmos:Uri");
    await TryLoadSecretAsync(client, configuration, "KeyVault:JwtSigningKeySecretName", "Jwt:SigningKey");
    await TryLoadSecretAsync(client, configuration, "KeyVault:BlobAccountUrlSecretName", "Blob:AccountUrl");
}

static async Task TryLoadSecretAsync(SecretClient client, IConfiguration configuration, string configKey, string targetKey)
{
    var secretName = configuration[configKey];
    if (string.IsNullOrWhiteSpace(secretName))
        return;

    var value = await TryGetSecretAsync(client, secretName);
    if (value is not null)
        configuration[targetKey] = value;
}

static async Task<string?> TryGetSecretAsync(SecretClient client, string secretName)
{
    try
    {
        var secret = await client.GetSecretAsync(secretName);
        return secret.Value.Value;
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        return null;
    }
}
