using System.Reflection;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Persistence;

public sealed class CosmosConfigRepositoryTests
{
    private static readonly Type ConfigDocumentType = typeof(CosmosConfigRepository)
        .GetNestedType("ConfigDocument", BindingFlags.NonPublic)!;

    [Fact]
    public void Defaults_ExtractionModel_IsGpt55()
    {
        var defaultConfig = InvokeCreateDefaults();

        defaultConfig.ExtractionModel.Should().Be("gpt-5.5");
    }

    [Fact]
    public void Defaults_PrimaryEvidenceModel_IsGpt54()
    {
        var defaultConfig = InvokeCreateDefaults();

        defaultConfig.PrimaryEvidenceModel.Should().Be("gpt-5.4");
    }

    [Fact]
    public void Defaults_ScoringModel_IsGpt55()
    {
        var defaultConfig = InvokeCreateDefaults();

        defaultConfig.ScoringModel.Should().Be("gpt-5.5");
    }

    [Fact]
    public void MapToDomain_LegacyDisallowedEvidenceModel_CoercesToDefault()
    {
        var doc = BuildConfigDocument(primaryEvidenceModel: "gpt-5.5");

        var config = InvokeMapToDomain(doc);

        config.PrimaryEvidenceModel.Should().Be("gpt-5.4");
    }

    [Fact]
    public void MapToDomain_LegacyMissingEvidenceModel_FallsBackToDefault()
    {
        var doc = BuildConfigDocument(primaryEvidenceModel: null);

        var config = InvokeMapToDomain(doc);

        config.PrimaryEvidenceModel.Should().Be("gpt-5.4");
    }

    [Fact]
    public void MapToDomain_AllowedEvidenceModel_PreservesStoredValue()
    {
        var doc = BuildConfigDocument(primaryEvidenceModel: "claude-opus-4-8");

        var config = InvokeMapToDomain(doc);

        config.PrimaryEvidenceModel.Should().Be("claude-opus-4-8");
    }

    [Fact]
    public void Defaults_SeedingMode_IsLegacy()
    {
        var defaultConfig = InvokeCreateDefaults();

        defaultConfig.SeedingMode.Should().Be(SeedingModes.Legacy);
    }

    [Fact]
    public void MapToDomain_MissingSeedingMode_FallsBackToLegacy()
    {
        var doc = BuildConfigDocument(primaryEvidenceModel: "gpt-5.4", seedingMode: null);

        var config = InvokeMapToDomain(doc);

        config.SeedingMode.Should().Be(SeedingModes.Legacy);
    }

    [Fact]
    public void MapToDomain_DeepSeedingMode_PreservesStoredValue()
    {
        var doc = BuildConfigDocument(primaryEvidenceModel: "gpt-5.4", seedingMode: "deep");

        var config = InvokeMapToDomain(doc);

        config.SeedingMode.Should().Be(SeedingModes.Deep);
    }

    [Fact]
    public void MapToDomain_UnknownSeedingMode_CoercesToLegacy()
    {
        var doc = BuildConfigDocument(primaryEvidenceModel: "gpt-5.4", seedingMode: "shallow");

        var config = InvokeMapToDomain(doc);

        config.SeedingMode.Should().Be(SeedingModes.Legacy);
    }

    private static ModelConfig InvokeCreateDefaults()
    {
        return (ModelConfig)typeof(CosmosConfigRepository)
            .GetMethod("CreateDefaults", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
    }

    private static ModelConfig InvokeMapToDomain(object configDocument)
    {
        return (ModelConfig)typeof(CosmosConfigRepository)
            .GetMethod("MapToDomain", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new[] { configDocument })!;
    }

    private static object BuildConfigDocument(string? primaryEvidenceModel, string? seedingMode = null)
    {
        var doc = Activator.CreateInstance(ConfigDocumentType)!;
        ConfigDocumentType.GetProperty("ExtractionModel")!.SetValue(doc, "gpt-5.5");
        ConfigDocumentType.GetProperty("PrimaryEvidenceModel")!.SetValue(doc, primaryEvidenceModel);
        ConfigDocumentType.GetProperty("ScoringModel")!.SetValue(doc, "gpt-5.5");
        ConfigDocumentType.GetProperty("SeedingMode")!.SetValue(doc, seedingMode);
        ConfigDocumentType.GetProperty("UpdatedAt")!.SetValue(doc, DateTimeOffset.UtcNow);
        return doc;
    }
}
