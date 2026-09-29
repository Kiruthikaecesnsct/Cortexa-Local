using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class ResultsDocumentsSerializationTests
{
    [Fact]
    public void CandidateResultDto_WithSourceStatus_DeserializesAllStates()
    {
        const string json = """
        {
            "id": "cand-001",
            "source_availability": {
                "patent_api": true,
                "vector_corpus": false,
                "llm_deep_research": true
            },
            "source_status": {
                "patent_api": "active",
                "vector_corpus": "filtered",
                "llm_deep_research": "timeout"
            }
        }
        """;

        var candidate = JsonSerializer.Deserialize<CandidateResultDto>(json);

        candidate.Should().NotBeNull();
        candidate!.SourceAvailability["patent_api"].Should().BeTrue();
        candidate.SourceAvailability["vector_corpus"].Should().BeFalse();
        candidate.SourceStatus["patent_api"].Should().Be("active");
        candidate.SourceStatus["vector_corpus"].Should().Be("filtered");
        candidate.SourceStatus["llm_deep_research"].Should().Be("timeout");
    }

    [Fact]
    public void CandidateResultDto_WithoutSourceStatus_DefaultsToEmptyDictionary()
    {
        const string json = """
        {
            "id": "cand-002",
            "source_availability": {
                "patent_api": true,
                "vector_corpus": true,
                "llm_deep_research": true
            }
        }
        """;

        var candidate = JsonSerializer.Deserialize<CandidateResultDto>(json);

        candidate.Should().NotBeNull();
        candidate!.SourceStatus.Should().NotBeNull();
        candidate.SourceStatus.Should().BeEmpty();
        candidate.SourceAvailability.Should().NotBeEmpty();
    }
}
