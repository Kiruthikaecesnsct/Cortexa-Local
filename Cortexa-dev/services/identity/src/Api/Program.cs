using System.Text;
using System.Threading.RateLimiting;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Cortexa.Identity.Api.Endpoints;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Infrastructure;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Sentry;

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

var connectionString = await LoadSecretAsync(
    vaultUri,
    builder.Configuration["KeyVault:ConnectionStringSecretName"]
        ?? throw new InvalidOperationException("KeyVault:ConnectionStringSecretName is required."));

var jwtSigningKey = await LoadOptionalSecretAsync(
    vaultUri,
    builder.Configuration["KeyVault:JwtSigningKeySecretName"]
        ?? "identity-jwt-signing-key",
    builder.Configuration["Jwt:SigningKey"] ?? string.Empty);

if (string.IsNullOrWhiteSpace(jwtSigningKey))
    throw new InvalidOperationException("Jwt:SigningKey must not be empty. Supply it via Key Vault or user-secrets.");

builder.Configuration["Jwt:SigningKey"] = jwtSigningKey;

var internalApiSharedSecret = await LoadOptionalSecretAsync(
    vaultUri,
    builder.Configuration["KeyVault:InternalApiSharedSecretSecretName"]
        ?? "identity-internal-api-shared-secret",
    builder.Configuration["Internal:SharedSecret"] ?? string.Empty);

if (string.IsNullOrWhiteSpace(internalApiSharedSecret))
    throw new InvalidOperationException(
        "Internal:SharedSecret must not be empty. Supply it via Key Vault or user-secrets.");

builder.Configuration["Internal:SharedSecret"] = internalApiSharedSecret;

var adminEmailSecretName = builder.Configuration["Seed:AdminEmailSecretName"]
    ?? "identity-admin-email";
var adminPasswordSecretName = builder.Configuration["Seed:AdminPasswordSecretName"]
    ?? "identity-admin-initial-password";

var adminEmail = await LoadOptionalSecretAsync(vaultUri, adminEmailSecretName, string.Empty);
var adminPassword = await LoadOptionalSecretAsync(vaultUri, adminPasswordSecretName, string.Empty);

if (!string.IsNullOrWhiteSpace(adminEmail))
    builder.Configuration["Seed:AdminEmail"] = adminEmail;

builder.Services.AddIdentityInfrastructure(connectionString, builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<RefreshHandler>();
builder.Services.AddScoped<EntraLoginHandler>();
builder.Services.AddScoped<LogoutHandler>();
builder.Services.AddScoped<RegisterHandler>();
builder.Services.AddScoped<GetPermissionsHandler>();
builder.Services.AddScoped<GetRolePermissionsHandler>();
builder.Services.AddScoped<UpdateRolePermissionsHandler>();
builder.Services.AddScoped<CreateOrgUserHandler>();
builder.Services.AddScoped<ListOrgUsersHandler>();
builder.Services.AddScoped<GetOrgUserHandler>();
builder.Services.AddScoped<DisableOrgUserHandler>();
builder.Services.AddScoped<EnableOrgUserHandler>();
builder.Services.AddScoped<ChangeOrgUserRoleHandler>();
builder.Services.AddScoped<UpdateProfileHandler>();
builder.Services.AddScoped<ChangePasswordHandler>();
builder.Services.AddScoped<ListAuditLogsHandler>();
builder.Services.AddScoped<UnlockOrgUserHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSigningKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireClaim("role", "SuperAdmin"));
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("role", "Admin"));
    options.AddPolicy("AdminOrSuperAdmin", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("role", "Admin") || ctx.User.HasClaim("role", "SuperAdmin")));
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.Window = TimeSpan.FromSeconds(60);
        limiterOptions.PermitLimit = 10;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("refresh", limiterOptions =>
    {
        limiterOptions.Window = TimeSpan.FromSeconds(60);
        limiterOptions.PermitLimit = 10;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("entra", limiterOptions =>
    {
        limiterOptions.Window = TimeSpan.FromSeconds(60);
        limiterOptions.PermitLimit = 10;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("register", limiterOptions =>
    {
        limiterOptions.Window = TimeSpan.FromSeconds(60);
        limiterOptions.PermitLimit = 5;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
    options.RejectionStatusCode = 429;
});

var app = builder.Build();

// Metrics are auto-enabled once the SDK is initialised; emit a startup counter.
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTRY_DSN")))
{
    SentrySdk.Metrics.EmitCounter("service.started", 1);
    SentrySdk.Logger.LogInfo("identity service started");
}

await MigrationRunner.MigrateAndSeedAsync(app.Services, adminPassword);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapAuthEndpoints();
app.MapAdminPermissionEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminAuditEndpoints();
app.MapAccountEndpoints();
app.MapInternalEndpoints();

app.Run();

static async Task<string> LoadSecretAsync(string vaultUri, string secretName)
{
    if (string.IsNullOrWhiteSpace(vaultUri))
        throw new InvalidOperationException("KeyVault:Uri must not be empty.");

    var client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
    var secret = await client.GetSecretAsync(secretName);
    return secret.Value.Value;
}

static async Task<string> LoadOptionalSecretAsync(string vaultUri, string secretName, string fallback)
{
    if (string.IsNullOrWhiteSpace(vaultUri))
        return fallback;

    try
    {
        var client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
        var secret = await client.GetSecretAsync(secretName);
        return secret.Value.Value;
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        return fallback;
    }
}
