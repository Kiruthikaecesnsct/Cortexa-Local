using System.Reflection;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Persistence;

public sealed class CosmosPatentSourceConfigRepositoryTests
{
    private static readonly Type DocumentType = typeof(CosmosPatentSourceConfigRepository)
        .GetNestedType("PatentSourcesDocument", BindingFlags.NonPublic)!;

    private static readonly Type SourcesSectionType = typeof(CosmosPatentSourceConfigRepository)
        .GetNestedType("SourcesSection", BindingFlags.NonPublic)!;

    private static readonly Type SourceFlagDocumentType = typeof(CosmosPatentSourceConfigRepository)
        .GetNestedType("SourceFlagDocument", BindingFlags.NonPublic)!;

    [Fact]
    public void CreateDefaults_AllSourcesEnabled_VersionOne()
    {
        var defaults = InvokeCreateDefaults();

        defaults.Uspto.Enabled.Should().BeTrue();
        defaults.Epo.Enabled.Should().BeTrue();
        defaults.Lens.Enabled.Should().BeTrue();
        defaults.ConfigVersion.Should().Be(1);
    }

    [Fact]
    public void MapToDocument_RoundTripsThroughMapToDomain_PreservesFlagsAndVersion()
    {
        var config = new PatentSourceConfig(
            Uspto: new PatentSourceFlag(true),
            Epo: new PatentSourceFlag(false),
            Lens: new PatentSourceFlag(true),
            ConfigVersion: 7,
            UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedBy: "alpha@example.com");

        var document = InvokeMapToDocument(config);
        var roundTripped = InvokeMapToDomain(document);

        roundTripped.Uspto.Enabled.Should().BeTrue();
        roundTripped.Epo.Enabled.Should().BeFalse();
        roundTripped.Lens.Enabled.Should().BeTrue();
        roundTripped.ConfigVersion.Should().Be(7);
        roundTripped.UpdatedBy.Should().Be("alpha@example.com");
    }

    [Fact]
    public void MapToDocument_UsesFrozenIdAndPartitionValue()
    {
        var config = new PatentSourceConfig(
            new PatentSourceFlag(true), new PatentSourceFlag(true), new PatentSourceFlag(true),
            1, DateTimeOffset.UtcNow, "system");

        var document = InvokeMapToDocument(config);

        GetProperty(document, "Id").Should().Be("patent-sources");
        GetProperty(document, "BatchId").Should().Be("global");
    }

    [Fact]
    public void MapToDocument_SourcesSection_UsesUsptoEpoLensKeys()
    {
        var config = new PatentSourceConfig(
            new PatentSourceFlag(true), new PatentSourceFlag(false), new PatentSourceFlag(true),
            1, DateTimeOffset.UtcNow, "system");

        var document = InvokeMapToDocument(config);
        var sources = GetProperty(document, "Sources")!;

        var uspto = GetProperty(sources, "Uspto")!;
        var epo = GetProperty(sources, "Epo")!;
        var lens = GetProperty(sources, "Lens")!;

        GetProperty(uspto, "Enabled").Should().Be(true);
        GetProperty(epo, "Enabled").Should().Be(false);
        GetProperty(lens, "Enabled").Should().Be(true);
    }

    [Fact]
    public void MapToDomain_MissingUpdatedBy_FallsBackToSystem()
    {
        var document = Activator.CreateInstance(DocumentType)!;
        SetProperty(document, "Sources", BuildSourcesSection(true, true, true));
        SetProperty(document, "ConfigVersion", 3);
        SetProperty(document, "UpdatedAt", DateTimeOffset.UtcNow);
        SetProperty(document, "UpdatedBy", null);

        var config = InvokeMapToDomain(document);

        config.UpdatedBy.Should().Be("system");
    }

    private static object BuildSourcesSection(bool uspto, bool epo, bool lens)
    {
        var section = Activator.CreateInstance(SourcesSectionType)!;
        SetProperty(section, "Uspto", Activator.CreateInstance(SourceFlagDocumentType, [uspto]));
        SetProperty(section, "Epo", Activator.CreateInstance(SourceFlagDocumentType, [epo]));
        SetProperty(section, "Lens", Activator.CreateInstance(SourceFlagDocumentType, [lens]));
        return section;
    }

    private static PatentSourceConfig InvokeCreateDefaults()
    {
        return (PatentSourceConfig)typeof(CosmosPatentSourceConfigRepository)
            .GetMethod("CreateDefaults", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
    }

    private static object InvokeMapToDocument(PatentSourceConfig config)
    {
        return typeof(CosmosPatentSourceConfigRepository)
            .GetMethod("MapToDocument", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [config])!;
    }

    private static PatentSourceConfig InvokeMapToDomain(object document)
    {
        return (PatentSourceConfig)typeof(CosmosPatentSourceConfigRepository)
            .GetMethod("MapToDomain", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [document])!;
    }

    private static object? GetProperty(object instance, string name) =>
        instance.GetType().GetProperty(name)!.GetValue(instance);

    private static void SetProperty(object instance, string name, object? value) =>
        instance.GetType().GetProperty(name)!.SetValue(instance, value);
}
