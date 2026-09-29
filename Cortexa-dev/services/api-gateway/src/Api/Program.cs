using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Sentry;
using Cortexa.ApiGateway.Api;
using Cortexa.ApiGateway.Api.Auth;
using Cortexa.ApiGateway.Api.Cors;
using Cortexa.ApiGateway.Api.Middleware;
using Cortexa.ApiGateway.Api.Transforms;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

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
{
    var secretClient = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());

    var secretName = builder.Configuration["KeyVault:JwtSigningKeySecretName"] ?? "jwt-signing-key";
    var secret = await secretClient.GetSecretAsync(secretName);
    builder.Configuration["Jwt:SigningKey"] = secret.Value.Value;

    var internalKeySecretName = builder.Configuration[$"{UserStatusSettings.SectionName}:InternalKeySecretName"];
    if (!string.IsNullOrWhiteSpace(internalKeySecretName))
    {
        var internalKeySecret = await secretClient.GetSecretAsync(internalKeySecretName);
        builder.Configuration[$"{UserStatusSettings.SectionName}:InternalKey"] = internalKeySecret.Value.Value;
    }
}

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms<ContextHeaderTransformProvider>();

builder.Services.AddSingleton<IProxyConfigFilter, DefaultClusterConfigFilter>();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.AddGatewayCors(builder.Configuration);
builder.Services.AddGatewayAuthentication(builder.Configuration);
builder.Services.AddGatewayAuthorization();

builder.Services
    .AddOptions<UserStatusSettings>()
    .Bind(builder.Configuration.GetSection(UserStatusSettings.SectionName))
    .Validate(s => !string.IsNullOrWhiteSpace(s.IdentityInternalBaseUrl), "UserStatus:IdentityInternalBaseUrl is required")
    .ValidateOnStart();

builder.Services.AddHttpClient<IUserStatusClient, UserStatusClient>((serviceProvider, httpClient) =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<UserStatusSettings>>().Value;
    httpClient.BaseAddress = new Uri(settings.IdentityInternalBaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
});

var app = builder.Build();

// Metrics are auto-enabled once the SDK is initialised; emit a startup counter.
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTRY_DSN")))
{
    SentrySdk.Metrics.EmitCounter("service.started", 1);
    SentrySdk.Logger.LogInfo("api-gateway service started");
}

app.UsePathBase("/api");

app.UseCors(Cortexa.ApiGateway.Api.Cors.CorsServiceCollectionExtensions.PolicyName);

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<AnonymousRouteMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();

app.UseAuthentication();
app.UseMiddleware<UserStatusMiddleware>();
app.UseAuthorization();

app.MapReverseProxy()
    .RequireAuthorization();

app.Run();

public partial class Program
{
}
