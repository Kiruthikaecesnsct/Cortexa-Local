using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.PatentApis;

// Self-contained live probe for the three patent source APIs. Every code path
// here either returns a boolean/coarse reason or throws — the credential value
// itself is never logged, wrapped into an exception message, or returned.
public sealed class PatentConnectionProbe : IPatentConnectionProbe
{
    private const string UsptoSearchPath = "/api/v1/patent/applications/search";
    private const string LensSearchPath = "/patent/search";

    private readonly HttpClient _httpClient;
    private readonly SecretClient? _secretClient;
    private readonly PatentApiSettings _settings;

    public PatentConnectionProbe(HttpClient httpClient, SecretClient? secretClient, IOptions<PatentApiSettings> settings)
    {
        _httpClient = httpClient;
        _secretClient = secretClient;
        _settings = settings.Value;
    }

    public async Task<PatentProbeResult> TestStoredAsync(PatentSource source, CancellationToken ct)
    {
        if (_secretClient is null)
            return PatentProbeResult.Failed("key_vault_unavailable");

        var material = await LoadStoredMaterialAsync(source, ct);
        if (material is null)
            return PatentProbeResult.Failed("not_configured");

        return await TestSuppliedAsync(source, material, ct);
    }

    public async Task<PatentProbeResult> TestSuppliedAsync(PatentSource source, PatentSecretMaterial credential, CancellationToken ct)
    {
        try
        {
            return source switch
            {
                PatentSource.Uspto => await ProbeUsptoAsync(credential.ApiKey, ct),
                PatentSource.Epo => await ProbeEpoAsync(credential.ConsumerKey, credential.OauthSecret, ct),
                PatentSource.Lens => await ProbeLensAsync(credential.ApiKey, ct),
                _ => PatentProbeResult.Failed("unsupported_source")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return PatentProbeResult.Failed("timeout");
        }
        catch (HttpRequestException)
        {
            return PatentProbeResult.Failed("network_error");
        }
        catch (Exception)
        {
            return PatentProbeResult.Failed("probe_error");
        }
    }

    private async Task<PatentSecretMaterial?> LoadStoredMaterialAsync(PatentSource source, CancellationToken ct) => source switch
    {
        PatentSource.Uspto => await BuildApiKeyMaterialAsync(PatentSecretNames.UsptoApiKey, ct),
        PatentSource.Lens => await BuildApiKeyMaterialAsync(PatentSecretNames.LensApiKey, ct),
        PatentSource.Epo => await BuildEpoMaterialAsync(ct),
        _ => null
    };

    private async Task<PatentSecretMaterial?> BuildApiKeyMaterialAsync(string secretName, CancellationToken ct)
    {
        var value = await TryGetSecretValueAsync(secretName, ct);
        return value is null ? null : new PatentSecretMaterial(value, null, null);
    }

    private async Task<PatentSecretMaterial?> BuildEpoMaterialAsync(CancellationToken ct)
    {
        var consumer = await TryGetSecretValueAsync(PatentSecretNames.EpoConsumerKey, ct);
        var oauth = await TryGetSecretValueAsync(PatentSecretNames.EpoOAuthSecret, ct);
        return consumer is null || oauth is null ? null : new PatentSecretMaterial(null, consumer, oauth);
    }

    private async Task<string?> TryGetSecretValueAsync(string secretName, CancellationToken ct)
    {
        try
        {
            var secret = await _secretClient!.GetSecretAsync(secretName, cancellationToken: ct);
            return secret.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<PatentProbeResult> ProbeUsptoAsync(string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return PatentProbeResult.Failed("missing_credential");

        var url = _settings.UsptoBaseUrl.TrimEnd('/') + UsptoSearchPath;
        var payload = new { q = "test", pagination = new { offset = 0, limit = 1 } };

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-API-KEY", apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        return ClassifyResponse(response.StatusCode);
    }

    private async Task<PatentProbeResult> ProbeEpoAsync(string? consumerKey, string? oauthSecret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(oauthSecret))
            return PatentProbeResult.Failed("missing_credential");

        var url = _settings.EpoBaseUrl.TrimEnd('/') + _settings.EpoTokenPath;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{consumerKey}:{oauthSecret}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await _httpClient.SendAsync(request, ct);
        return ClassifyResponse(response.StatusCode);
    }

    private async Task<PatentProbeResult> ProbeLensAsync(string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return PatentProbeResult.Failed("missing_credential");

        var url = _settings.LensBaseUrl.TrimEnd('/') + LensSearchPath;
        var payload = new { query = new { query_string = new { query = "test" } }, size = 1 };

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        return ClassifyResponse(response.StatusCode);
    }

    private static PatentProbeResult ClassifyResponse(HttpStatusCode statusCode)
    {
        if ((int)statusCode is >= 200 and < 300)
            return PatentProbeResult.Succeeded();

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return PatentProbeResult.Failed("unauthorized");

        return PatentProbeResult.Failed("unexpected_status");
    }
}
