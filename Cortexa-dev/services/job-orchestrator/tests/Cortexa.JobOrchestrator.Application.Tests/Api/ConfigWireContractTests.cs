using System.Text.Json;
using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Api.Endpoints;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Api;

/// <summary>
/// Locks the serialized /config wire contract. The Api serializes and binds all
/// HTTP JSON under JsonNamingPolicy.SnakeCaseLower (Program.cs), and the frontend
/// ModelConfigDto/Request read those exact snake_case keys. The GET body keys are the
/// real bug surface: the frontend reads them with case-sensitive JS property access,
/// so any drift away from snake_case silently loads undefined stage models again.
/// </summary>
public sealed class ConfigWireContractTests
{
    // Mirrors the real endpoint options: minimal APIs start from JsonSerializerDefaults.Web
    // (camelCase, case-insensitive reads), and Program.cs overrides the naming policy and
    // the null-ignore condition. Building the options the same way keeps these assertions
    // faithful to the bytes clients actually exchange.
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void GetConfigResponse_SerializesAllFields_AsSnakeCase()
    {
        var response = new ConfigResponse(
            "gpt-5.5", "gpt-5.4", "gpt-5.5", "gpt-5.5", "legacy", DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(response, WireOptions);
        using var doc = JsonDocument.Parse(json);

        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        keys.Should().BeEquivalentTo(new[]
        {
            "extraction_model",
            "primary_evidence_model",
            "scoring_model",
            "seeding_model",
            "seeding_mode",
            "updated_at",
        });
    }

    [Fact]
    public void GetConfigResponse_CarriesModelValues_UnderSnakeCaseKeys()
    {
        var response = new ConfigResponse(
            "gpt-5.5", "grok-4.3", "gpt-5.4", "claude-opus-4-8", "deep", DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(response, WireOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("extraction_model").GetString().Should().Be("gpt-5.5");
        root.GetProperty("primary_evidence_model").GetString().Should().Be("grok-4.3");
        root.GetProperty("scoring_model").GetString().Should().Be("gpt-5.4");
        root.GetProperty("seeding_model").GetString().Should().Be("claude-opus-4-8");
        root.GetProperty("seeding_mode").GetString().Should().Be("deep");
    }

    [Fact]
    public void PutConfigRequest_BindsSnakeCaseBody_ToAllFields()
    {
        const string body = """
        {
          "extraction_model": "gpt-5.5",
          "primary_evidence_model": "gpt-5.4",
          "scoring_model": "gpt-5.5",
          "seeding_model": "grok-4.3",
          "seeding_mode": "deep"
        }
        """;

        var request = JsonSerializer.Deserialize<ConfigRequest>(body, WireOptions);

        request.Should().NotBeNull();
        request!.ExtractionModel.Should().Be("gpt-5.5");
        request.PrimaryEvidenceModel.Should().Be("gpt-5.4");
        request.ScoringModel.Should().Be("gpt-5.5");
        request.SeedingModel.Should().Be("grok-4.3");
        request.SeedingMode.Should().Be("deep");
    }

    [Fact]
    public void PutConfigRequest_OmittedSeedingMode_BindsAsNull()
    {
        // seeding_mode is optional on the wire; the endpoint defaults a null to legacy.
        const string body = """
        {
          "extraction_model": "gpt-5.5",
          "primary_evidence_model": "gpt-5.4",
          "scoring_model": "gpt-5.5",
          "seeding_model": "gpt-5.5"
        }
        """;

        var request = JsonSerializer.Deserialize<ConfigRequest>(body, WireOptions);

        request!.ExtractionModel.Should().Be("gpt-5.5");
        request.SeedingMode.Should().BeNull();
    }
}
