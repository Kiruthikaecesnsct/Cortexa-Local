using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests;

public sealed class CosmosResultsReadRepositoryTests
{
    private const string DatabaseName = "cortexa-pipeline";
    private const string ReportsContainerName = "reports";

    private static CosmosResultsReadRepository BuildRepository(out Container container)
    {
        container = Substitute.For<Container>();
        var client = Substitute.For<CosmosClient>();
        client.GetContainer(DatabaseName, ReportsContainerName).Returns(container);

        var settings = Options.Create(new CosmosSettings
        {
            Database = DatabaseName,
            HarvestingContainer = ReportsContainerName,
            SeedingContainer = ReportsContainerName,
            CandidatesContainer = "candidates",
            VerdictsContainer = "verdicts",
            EvidenceBundlesContainer = "evidence_bundles"
        });

        return new CosmosResultsReadRepository(client, settings);
    }

    private static CosmosResultsReadRepository BuildRepository(out Container reportsContainer, out CosmosClient client)
    {
        reportsContainer = Substitute.For<Container>();
        client = Substitute.For<CosmosClient>();
        client.GetContainer(DatabaseName, ReportsContainerName).Returns(reportsContainer);

        var settings = Options.Create(new CosmosSettings
        {
            Database = DatabaseName,
            HarvestingContainer = ReportsContainerName,
            SeedingContainer = ReportsContainerName,
            CandidatesContainer = "candidates",
            VerdictsContainer = "verdicts",
            EvidenceBundlesContainer = "evidence_bundles"
        });

        return new CosmosResultsReadRepository(client, settings);
    }

    [Fact]
    public async Task GetHarvestingResultAsync_FiltersToHarvestingEngine()
    {
        var repository = BuildRepository(out var container);

        var harvestingDoc = new HarvestingResultRecord
        {
            Id = "report-001",
            BatchId = "batch-123",
            DocumentId = "doc-xyz",
            Engine = "harvesting",
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Candidates = []
        };

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { Candidates = harvestingDoc.Candidates }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        container.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var iterator = Substitute.For<FeedIterator<HarvestingResultRecord>>();
        iterator.HasMoreResults.Returns(true, false);

        var page = Substitute.For<FeedResponse<HarvestingResultRecord>>();
        page.GetEnumerator().Returns(_ => new List<HarvestingResultRecord> { harvestingDoc }.GetEnumerator());

        iterator.ReadNextAsync(Arg.Any<CancellationToken>())
            .Returns(page);

        QueryDefinition? capturedQuery = null;
        container.GetItemQueryIterator<HarvestingResultRecord>(
            Arg.Do<QueryDefinition>(q => capturedQuery = q),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var result = await repository.GetHarvestingResultAsync("batch-123", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Engine.Should().Be("harvesting");
        result.DocumentId.Should().Be("doc-xyz");

        capturedQuery.Should().NotBeNull();
        var parameters = capturedQuery!.GetQueryParameters();
        parameters.Should().Contain(p => p.Name == "@engine" && p.Value.Equals("harvesting"));
        parameters.Should().Contain(p => p.Name == "@batchId" && p.Value.Equals("batch-123"));
    }

    [Fact]
    public async Task GetSeedingResultAsync_FiltersToSeedingEngine()
    {
        var repository = BuildRepository(out var container);

        var seedingDoc = new SeedingReportRecord
        {
            Id = "report-002",
            BatchId = "batch-456",
            Engine = "seeding",
            Opportunities = [],
            CreatedAt = DateTime.UtcNow.ToString("o")
        };

        var iterator = Substitute.For<FeedIterator<SeedingReportRecord>>();
        iterator.HasMoreResults.Returns(true, false);

        var page = Substitute.For<FeedResponse<SeedingReportRecord>>();
        page.GetEnumerator().Returns(_ => new List<SeedingReportRecord> { seedingDoc }.GetEnumerator());

        iterator.ReadNextAsync(Arg.Any<CancellationToken>())
            .Returns(page);

        QueryDefinition? capturedQuery = null;
        container.GetItemQueryIterator<SeedingReportRecord>(
            Arg.Do<QueryDefinition>(q => capturedQuery = q),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var result = await repository.GetSeedingResultAsync("batch-456", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Engine.Should().Be("seeding");

        capturedQuery.Should().NotBeNull();
        var parameters = capturedQuery!.GetQueryParameters();
        parameters.Should().Contain(p => p.Name == "@engine" && p.Value.Equals("seeding"));
        parameters.Should().Contain(p => p.Name == "@batchId" && p.Value.Equals("batch-456"));
    }

    [Fact]
    public async Task GetHarvestingResultAsync_ReturnsNull_WhenNoResults()
    {
        var repository = BuildRepository(out var container);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(false);

        container.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var result = await repository.GetHarvestingResultAsync("batch-999", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSeedingResultAsync_ReturnsNull_WhenNoResults()
    {
        var repository = BuildRepository(out var container);

        var iterator = Substitute.For<FeedIterator<SeedingReportRecord>>();
        iterator.HasMoreResults.Returns(false);

        container.GetItemQueryIterator<SeedingReportRecord>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var result = await repository.GetSeedingResultAsync("batch-999", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetHarvestingResultAsync_DoesNotReturnSeedingResults()
    {
        var repository = BuildRepository(out var container);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(false);

        container.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var result = await repository.GetHarvestingResultAsync("batch-with-only-seeding", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetHarvestingResultAsync_DeserializesRichCandidateFields()
    {
        var repository = BuildRepository(out var container);

        var richCandidate = new CandidateResultDto
        {
            CandidateId = "cand-001",
            Title = "Rich Invention",
            Description = "A fully-ranked candidate with all fields",
            Maturity = "Mature",
            Rank = 1,
            WeightedScore = 72.5,
            Axes = new Dictionary<string, HarvestingAxisScoreDto>
            {
                ["Novelty"] = new() { Axis = "Novelty", Score = 80, Refs = ["ref-1"] },
                ["Inventiveness"] = new() { Axis = "Inventiveness", Score = 70, Refs = [] },
                ["Commercial"] = new() { Axis = "Commercial", Score = 75, Refs = [] },
                ["Strategic"] = new() { Axis = "Strategic", Score = 68, Refs = ["ref-2", "ref-3"] },
                ["Patentability"] = new() { Axis = "Patentability", Score = 90, Refs = ["ref-4"] }
            },
            AgreementFlag = "full",
            Citations =
            [
                new CitationDto { Ref = "E1", SourceType = "patent_api", Title = "Patent A", PatentId = "US123", Url = "https://example.com/patent/123", Similarity = 0.91 },
                new CitationDto { Ref = "E2", SourceType = "vector_corpus", Title = "Patent B", Url = "https://example.com/patent/456", Similarity = 0.82 }
            ],
            ProvenanceLinks =
            [
                new ProvenanceLinkDto { DocumentId = "chunk-001", Locator = "p1:100-200", SourceKind = "chunk", HitUrl = null },
                new ProvenanceLinkDto { DocumentId = "chunk-002", Locator = "p2:50-150", SourceKind = "chunk", HitUrl = "https://example.com/doc-2" }
            ]
        };

        var harvestingDoc = new HarvestingResultRecord
        {
            Id = "report-002",
            BatchId = "batch-456",
            DocumentId = "doc-abc",
            Engine = "harvesting",
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Candidates = [richCandidate]
        };

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { Candidates = harvestingDoc.Candidates }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        container.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var iterator = Substitute.For<FeedIterator<HarvestingResultRecord>>();
        iterator.HasMoreResults.Returns(true, false);

        var page = Substitute.For<FeedResponse<HarvestingResultRecord>>();
        page.GetEnumerator().Returns(_ => new List<HarvestingResultRecord> { harvestingDoc }.GetEnumerator());

        iterator.ReadNextAsync(Arg.Any<CancellationToken>())
            .Returns(page);

        container.GetItemQueryIterator<HarvestingResultRecord>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(iterator);

        var result = await repository.GetHarvestingResultAsync("batch-456", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(1);

        var candidate = result.Candidates[0];
        candidate.CandidateId.Should().Be("cand-001");
        candidate.Title.Should().Be("Rich Invention");
        candidate.Description.Should().Be("A fully-ranked candidate with all fields");
        candidate.Maturity.Should().Be("Mature");
        candidate.Rank.Should().Be(1);
        candidate.WeightedScore.Should().Be(72.5);
        candidate.Axes.Should().HaveCount(5);
        candidate.Axes["Novelty"].Score.Should().Be(80);
        candidate.Axes["Novelty"].Refs.Should().ContainSingle().Which.Should().Be("ref-1");
        candidate.Axes["Patentability"].Score.Should().Be(90);
        candidate.AgreementFlag.Should().Be("full");
        candidate.Citations.Should().HaveCount(2);
        candidate.ProvenanceLinks.Should().HaveCount(2);
    }

    [Fact]
    public void GoldenHarvestingReport_WithObjectShapedCitationsAndProvenance_DeserializesWithoutThrowing()
    {
        const string goldenReportJson = """
        {
            "id": "report-003",
            "batch_id": "batch-789",
            "document_id": "doc-golden",
            "engine": "harvesting",
            "generated_at": "2026-07-07T00:00:00Z",
            "candidates": [
                {
                    "id": "c-1",
                    "title": "Adaptive Signal Compression Method",
                    "abstract": "A method for compressing streaming sensor signals.",
                    "description": "Full description text.",
                    "claim_draft": "A method comprising...",
                    "novelty_hypothesis": "Novel adaptive windowing.",
                    "source_asset_id": "asset-1",
                    "batch_id": "batch-789",
                    "created_at": "2026-07-07T00:00:00Z",
                    "candidate_id": "cand-golden-1",
                    "maturity": "Mature",
                    "rank": 1,
                    "weighted_score": 81.5,
                    "axes": {
                        "Novelty": { "axis": "Novelty", "score": 85, "refs": ["E1"] }
                    },
                    "agreement_flag": "full",
                    "citations": [
                        {
                            "ref": "E1",
                            "source_type": "patent_api",
                            "title": "Signal Compression Patent",
                            "patent_id": "US10999999",
                            "url": "https://patents.example.com/US10999999",
                            "similarity": 0.87
                        },
                        {
                            "ref": "E2",
                            "source_type": "vector_corpus",
                            "title": "Related Corpus Entry",
                            "patent_id": null,
                            "url": "",
                            "similarity": 0.63
                        }
                    ],
                    "provenance_links": [
                        {
                            "document_id": "doc-golden",
                            "locator": "page:3;span:100-400",
                            "source_kind": "chunk",
                            "hit_url": null
                        },
                        {
                            "document_id": "doc-golden",
                            "locator": "page:5;span:20-80",
                            "source_kind": "chunk",
                            "hit_url": "https://storage.example.com/doc-golden#page5"
                        }
                    ]
                }
            ]
        }
        """;

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };

        var serializer = new SystemTextJsonCosmosSerializer(jsonOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(goldenReportJson));

        var act = () => serializer.FromStream<HarvestingResultRecord>(stream);

        var record = act.Should().NotThrow().Subject;

        record.Should().NotBeNull();
        record.Candidates.Should().HaveCount(1);

        var candidate = record.Candidates[0];
        candidate.Citations.Should().HaveCount(2);
        candidate.Citations[0].Ref.Should().Be("E1");
        candidate.Citations[0].SourceType.Should().Be("patent_api");
        candidate.Citations[0].PatentId.Should().Be("US10999999");
        candidate.Citations[0].Similarity.Should().Be(0.87);
        candidate.Citations[1].PatentId.Should().BeNull();

        candidate.ProvenanceLinks.Should().HaveCount(2);
        candidate.ProvenanceLinks[0].DocumentId.Should().Be("doc-golden");
        candidate.ProvenanceLinks[0].SourceKind.Should().Be("chunk");
        candidate.ProvenanceLinks[0].HitUrl.Should().BeNull();
        candidate.ProvenanceLinks[1].HitUrl.Should().Be("https://storage.example.com/doc-golden#page5");
    }

    [Fact]
    public async Task GetCandidateDetailAsync_ReturnsRichCandidateFromHarvestingReport()
    {
        var repository = BuildRepository(out var reportsContainer, out var client);

        var candidatesContainer = Substitute.For<Container>();
        var verdictsContainer = Substitute.For<Container>();
        client.GetContainer(DatabaseName, "candidates").Returns(candidatesContainer);
        client.GetContainer(DatabaseName, "verdicts").Returns(verdictsContainer);

        var harvestingDoc = new HarvestingResultRecord
        {
            Id = "report-001",
            BatchId = "batch-123",
            DocumentId = "doc-xyz",
            Engine = "harvesting",
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Candidates =
            [
                new CandidateResultDto
                {
                    Id = "cand-001",
                    CandidateId = "cand-001",
                    Title = "Rich Candidate",
                    Abstract = "Abstract text",
                    ClaimDraft = "Original claim from report",
                    WeightedScore = 78.5,
                    Axes = new Dictionary<string, HarvestingAxisScoreDto>
                    {
                        ["novelty"] = new() { Axis = "novelty", Score = 80, Refs = ["ref-1", "ref-2"] }
                    },
                    Citations =
                    [
                        new CitationDto
                        {
                            Ref = "ref-1",
                            SourceType = "patent_api",
                            Title = "Prior Patent",
                            PatentId = "US123456",
                            Url = "https://example.com/US123456",
                            Similarity = 0.92
                        }
                    ],
                    ProvenanceLinks =
                    [
                        new ProvenanceLinkDto
                        {
                            DocumentId = "doc-xyz",
                            Locator = "chars:100-200",
                            SourceKind = "pdf",
                            PageNumber = 5
                        }
                    ],
                    SourceAvailability = new Dictionary<string, bool>
                    {
                        ["patent_api"] = true,
                        ["vector_corpus"] = false,
                        ["llm_deep_research"] = true
                    }
                }
            ]
        };

        reportsContainer.ReadItemAsync<ReportCandidateDto>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<ReportCandidateDto>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        var candidateIterator = Substitute.For<FeedIterator<ReportCandidateDto>>();
        candidateIterator.HasMoreResults.Returns(false);
        reportsContainer.GetItemQueryIterator<ReportCandidateDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(candidateIterator);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { Candidates = harvestingDoc.Candidates }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        reportsContainer.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var iterator = Substitute.For<FeedIterator<HarvestingResultRecord>>();
        iterator.HasMoreResults.Returns(true, false);
        var page = Substitute.For<FeedResponse<HarvestingResultRecord>>();
        page.GetEnumerator().Returns(_ => new List<HarvestingResultRecord> { harvestingDoc }.GetEnumerator());
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(page);
        reportsContainer.GetItemQueryIterator<HarvestingResultRecord>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(iterator);

        verdictsContainer.ReadItemAsync<VerdictResultDto>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<VerdictResultDto>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        candidatesContainer.ReadItemAsync<CandidateDocument>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<CandidateDocument>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        var result = await repository.GetCandidateDetailAsync("batch-123", "cand-001", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("cand-001");
        result.Title.Should().Be("Rich Candidate");
        result.ClaimDraft.Should().Be("Original claim from report");
        result.Axes.Should().ContainKey("novelty");
        result.Citations.Should().HaveCount(1);
        result.Citations[0].Ref.Should().Be("ref-1");
        result.ProvenanceLinks.Should().HaveCount(1);
        result.SourceAvailability["patent_api"].Should().BeTrue();
        result.SourceAvailability["llm_deep_research"].Should().BeTrue();
    }

    [Fact]
    public async Task GetCandidateDetailAsync_ThreadsClaimFromVerdictOverReport()
    {
        var repository = BuildRepository(out var reportsContainer, out var client);

        var candidatesContainer = Substitute.For<Container>();
        var verdictsContainer = Substitute.For<Container>();
        client.GetContainer(DatabaseName, "candidates").Returns(candidatesContainer);
        client.GetContainer(DatabaseName, "verdicts").Returns(verdictsContainer);

        var harvestingDoc = new HarvestingResultRecord
        {
            Id = "report-002",
            BatchId = "batch-456",
            DocumentId = "doc-abc",
            Engine = "harvesting",
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Candidates =
            [
                new CandidateResultDto
                {
                    Id = "cand-002",
                    CandidateId = "cand-002",
                    Title = "Candidate With Verdicted Claim",
                    ClaimDraft = "Original claim"
                }
            ]
        };

        reportsContainer.ReadItemAsync<ReportCandidateDto>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<ReportCandidateDto>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        var candidateIterator = Substitute.For<FeedIterator<ReportCandidateDto>>();
        candidateIterator.HasMoreResults.Returns(false);
        reportsContainer.GetItemQueryIterator<ReportCandidateDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(candidateIterator);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { Candidates = harvestingDoc.Candidates }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        reportsContainer.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var iterator = Substitute.For<FeedIterator<HarvestingResultRecord>>();
        iterator.HasMoreResults.Returns(true, false);
        var page = Substitute.For<FeedResponse<HarvestingResultRecord>>();
        page.GetEnumerator().Returns(_ => new List<HarvestingResultRecord> { harvestingDoc }.GetEnumerator());
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(page);
        reportsContainer.GetItemQueryIterator<HarvestingResultRecord>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(iterator);

        var verdictResponse = Substitute.For<ItemResponse<VerdictResultDto>>();
        verdictResponse.Resource.Returns(new VerdictResultDto
        {
            CandidateId = "cand-002",
            DraftedClaim = "Drafted claim from verdict"
        });
        verdictsContainer.ReadItemAsync<VerdictResultDto>(
            "cand-002",
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(verdictResponse);

        candidatesContainer.ReadItemAsync<CandidateDocument>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<CandidateDocument>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        var result = await repository.GetCandidateDetailAsync("batch-456", "cand-002", CancellationToken.None);

        result.Should().NotBeNull();
        result!.ClaimDraft.Should().Be("Drafted claim from verdict");
    }

    [Fact]
    public async Task GetCandidateDetailAsync_FallsBackToCandidateDocClaimWhenVerdictHasNone()
    {
        var repository = BuildRepository(out var reportsContainer, out var client);

        var candidatesContainer = Substitute.For<Container>();
        var verdictsContainer = Substitute.For<Container>();
        client.GetContainer(DatabaseName, "candidates").Returns(candidatesContainer);
        client.GetContainer(DatabaseName, "verdicts").Returns(verdictsContainer);

        var harvestingDoc = new HarvestingResultRecord
        {
            Id = "report-003",
            BatchId = "batch-789",
            DocumentId = "doc-def",
            Engine = "harvesting",
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Candidates =
            [
                new CandidateResultDto
                {
                    Id = "cand-003",
                    CandidateId = "cand-003",
                    Title = "Candidate Claim Fallback",
                    ClaimDraft = "Original claim"
                }
            ]
        };

        reportsContainer.ReadItemAsync<ReportCandidateDto>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<ReportCandidateDto>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        var candidateIterator = Substitute.For<FeedIterator<ReportCandidateDto>>();
        candidateIterator.HasMoreResults.Returns(false);
        reportsContainer.GetItemQueryIterator<ReportCandidateDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(candidateIterator);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { Candidates = harvestingDoc.Candidates }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        reportsContainer.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var iterator = Substitute.For<FeedIterator<HarvestingResultRecord>>();
        iterator.HasMoreResults.Returns(true, false);
        var page = Substitute.For<FeedResponse<HarvestingResultRecord>>();
        page.GetEnumerator().Returns(_ => new List<HarvestingResultRecord> { harvestingDoc }.GetEnumerator());
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(page);
        reportsContainer.GetItemQueryIterator<HarvestingResultRecord>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(iterator);

        var verdictResponse = Substitute.For<ItemResponse<VerdictResultDto>>();
        verdictResponse.Resource.Returns(new VerdictResultDto
        {
            CandidateId = "cand-003",
            DraftedClaim = null
        });
        verdictsContainer.ReadItemAsync<VerdictResultDto>(
            "cand-003",
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(verdictResponse);

        var candidateResponse = Substitute.For<ItemResponse<CandidateDocument>>();
        candidateResponse.Resource.Returns(new CandidateDocument
        {
            Id = "cand-003",
            ClaimDraft = "Candidate doc claim"
        });
        candidatesContainer.ReadItemAsync<CandidateDocument>(
            "cand-003",
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(candidateResponse);

        var result = await repository.GetCandidateDetailAsync("batch-789", "cand-003", CancellationToken.None);

        result.Should().NotBeNull();
        result!.ClaimDraft.Should().Be("Candidate doc claim");
    }

    [Fact]
    public async Task GetCandidateDetailAsync_ReturnsNull_WhenReportMissing()
    {
        var repository = BuildRepository(out var reportsContainer, out var client);
        client.GetContainer(DatabaseName, ReportsContainerName).Returns(reportsContainer);

        reportsContainer.ReadItemAsync<ReportCandidateDto>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<ReportCandidateDto>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        var candidateIterator = Substitute.For<FeedIterator<ReportCandidateDto>>();
        candidateIterator.HasMoreResults.Returns(false);
        reportsContainer.GetItemQueryIterator<ReportCandidateDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(candidateIterator);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(false);
        reportsContainer.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var legacyIterator = Substitute.For<FeedIterator<HarvestingResultRecord>>();
        legacyIterator.HasMoreResults.Returns(false);
        reportsContainer.GetItemQueryIterator<HarvestingResultRecord>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(legacyIterator);

        var result = await repository.GetCandidateDetailAsync("batch-999", "cand-999", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCandidateDetailAsync_ReturnsNull_WhenCandidateNotInReport()
    {
        var repository = BuildRepository(out var reportsContainer);

        var harvestingDoc = new HarvestingResultRecord
        {
            Id = "report-004",
            BatchId = "batch-111",
            DocumentId = "doc-ghi",
            Engine = "harvesting",
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Candidates =
            [
                new CandidateResultDto
                {
                    Id = "cand-100",
                    CandidateId = "cand-100",
                    Title = "Different Candidate"
                }
            ]
        };

        reportsContainer.ReadItemAsync<ReportCandidateDto>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<ReportCandidateDto>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        var candidateIterator = Substitute.For<FeedIterator<ReportCandidateDto>>();
        candidateIterator.HasMoreResults.Returns(false);
        reportsContainer.GetItemQueryIterator<ReportCandidateDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(candidateIterator);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { Candidates = harvestingDoc.Candidates }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        reportsContainer.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>())
            .Returns(envelopeIterator);

        var iterator = Substitute.For<FeedIterator<HarvestingResultRecord>>();
        iterator.HasMoreResults.Returns(true, false);
        var page = Substitute.For<FeedResponse<HarvestingResultRecord>>();
        page.GetEnumerator().Returns(_ => new List<HarvestingResultRecord> { harvestingDoc }.GetEnumerator());
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(page);
        reportsContainer.GetItemQueryIterator<HarvestingResultRecord>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(iterator);

        var result = await repository.GetCandidateDetailAsync("batch-111", "cand-999", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetHarvestingResultAsync_ReassemblesSplitReport_WithHeaderAndMultipleCandidates()
    {
        var repository = BuildRepository(out var container, out var client);
        client.GetContainer(DatabaseName, ReportsContainerName).Returns(container);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { DocType = "report_header" },
            new() { DocType = "report_candidate" },
            new() { DocType = "report_candidate" }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        container.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(), default(string), Arg.Any<QueryRequestOptions>()).Returns(envelopeIterator);

        var headerIterator = Substitute.For<FeedIterator<ReportHeaderDto>>();
        headerIterator.HasMoreResults.Returns(true, false);
        var headerPage = Substitute.For<FeedResponse<ReportHeaderDto>>();
        headerPage.GetEnumerator().Returns(_ => new List<ReportHeaderDto>
        {
            new()
            {
                Id = "report-split-001",
                BatchId = "batch-split-123",
                DocumentId = "doc-split-xyz",
                Engine = "harvesting",
                DocType = "report_header",
                GeneratedAt = "2026-07-13T12:00:00Z",
                CandidateCount = 2
            }
        }.GetEnumerator());
        headerIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(headerPage);

        container.GetItemQueryIterator<ReportHeaderDto>(
            Arg.Any<QueryDefinition>(), default(string), Arg.Any<QueryRequestOptions>()).Returns(headerIterator);

        var candidateIterator = Substitute.For<FeedIterator<ReportCandidateDto>>();
        candidateIterator.HasMoreResults.Returns(true, false);
        var candidatePage = Substitute.For<FeedResponse<ReportCandidateDto>>();
        candidatePage.GetEnumerator().Returns(_ => new List<ReportCandidateDto>
        {
            new()
            {
                CandidateId = "cand-split-001",
                Title = "First Candidate",
                Description = "First description",
                Rank = 2,
                WeightedScore = 70.0,
                Axes = new Dictionary<string, HarvestingAxisScoreDto>
                {
                    ["Novelty"] = new() { Axis = "Novelty", Score = 75, Refs = ["ref-1"] }
                },
                Citations = new List<CitationDto>
                {
                    new() { Ref = "ref-1", SourceType = "patent_api", Title = "Patent X", Url = "https://example.com/x", Similarity = 0.85 }
                },
                ProvenanceLinks = new List<ProvenanceLinkDto>
                {
                    new()
                    {
                        DocumentId = "doc-split-xyz",
                        Locator = "page:1",
                        SourceKind = "pdf",
                        PageNumber = 1,
                        PageDimensions = new List<PageDimensionDto> { new() { PageNumber = 1, Width = 612, Height = 792 } },
                        HighlightRects = new List<HighlightRectDto> { new() { PageNumber = 1, X0 = 0.1, X1 = 0.9, Top = 0.2, Bottom = 0.3 } },
                        CleanExcerpt = "Clean excerpt text"
                    }
                }
            },
            new()
            {
                CandidateId = "cand-split-002",
                Title = "Second Candidate",
                Description = "Second description",
                Rank = 1,
                WeightedScore = 85.0,
                Axes = new Dictionary<string, HarvestingAxisScoreDto>
                {
                    ["Patentability"] = new() { Axis = "Patentability", Score = 90, Refs = ["ref-2"] }
                },
                Citations = new List<CitationDto>
                {
                    new() { Ref = "ref-2", SourceType = "vector_corpus", Title = "Corpus Entry Y", Url = "https://example.com/y", Similarity = 0.92 }
                },
                ProvenanceLinks = new List<ProvenanceLinkDto>
                {
                    new()
                    {
                        DocumentId = "doc-split-xyz",
                        Locator = "lines:10-20",
                        SourceKind = "code",
                        FilePath = "src/main.rs",
                        LineRange = new LineRangeDto { StartLine = 10, EndLine = 20 },
                        CleanExcerpt = "fn main() { ... }"
                    }
                }
            }
        }.GetEnumerator());
        candidateIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(candidatePage);

        container.GetItemQueryIterator<ReportCandidateDto>(
            Arg.Any<QueryDefinition>(), default(string), Arg.Any<QueryRequestOptions>()).Returns(candidateIterator);

        var result = await repository.GetHarvestingResultAsync("batch-split-123", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("report-split-001");
        result.BatchId.Should().Be("batch-split-123");
        result.DocumentId.Should().Be("doc-split-xyz");
        result.Engine.Should().Be("harvesting");
        result.GeneratedAt.Should().Be("2026-07-13T12:00:00Z");
        result.Candidates.Should().HaveCount(2);

        result.Candidates[0].CandidateId.Should().Be("cand-split-002");
        result.Candidates[0].Rank.Should().Be(1);
        result.Candidates[0].WeightedScore.Should().Be(85.0);
        result.Candidates[0].ProvenanceLinks.Should().HaveCount(1);
        result.Candidates[0].ProvenanceLinks[0].FilePath.Should().Be("src/main.rs");
        result.Candidates[0].ProvenanceLinks[0].LineRange!.StartLine.Should().Be(10);
        result.Candidates[0].ProvenanceLinks[0].CleanExcerpt.Should().Be("fn main() { ... }");

        result.Candidates[1].CandidateId.Should().Be("cand-split-001");
        result.Candidates[1].Rank.Should().Be(2);
        result.Candidates[1].WeightedScore.Should().Be(70.0);
        result.Candidates[1].ProvenanceLinks.Should().HaveCount(1);
        result.Candidates[1].ProvenanceLinks[0].PageDimensions.Should().HaveCount(1);
        result.Candidates[1].ProvenanceLinks[0].PageDimensions![0].Width.Should().Be(612);
        result.Candidates[1].ProvenanceLinks[0].HighlightRects.Should().HaveCount(1);
        result.Candidates[1].ProvenanceLinks[0].HighlightRects![0].X0.Should().Be(0.1);
        result.Candidates[1].ProvenanceLinks[0].CleanExcerpt.Should().Be("Clean excerpt text");
    }

    [Fact]
    public async Task GetHarvestingResultAsync_ReturnsNull_WhenHeaderAbsent()
    {
        var repository = BuildRepository(out var container, out var client);
        client.GetContainer(DatabaseName, ReportsContainerName).Returns(container);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { DocType = "report_candidate" }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        container.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Is<QueryDefinition>(q => q.QueryText.Contains("NOT IS_DEFINED(c.engine)")),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(envelopeIterator);

        var headerIterator = Substitute.For<FeedIterator<ReportHeaderDto>>();
        headerIterator.HasMoreResults.Returns(true, false);
        var headerPage = Substitute.For<FeedResponse<ReportHeaderDto>>();
        headerPage.GetEnumerator().Returns(_ => new List<ReportHeaderDto>().GetEnumerator());
        headerIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(headerPage);

        container.GetItemQueryIterator<ReportHeaderDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(headerIterator);

        var result = await repository.GetHarvestingResultAsync("batch-no-header", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetHarvestingResultAsync_HandlesSplitReportWithProvenance_US122Fields()
    {
        var repository = BuildRepository(out var container, out var client);
        client.GetContainer(DatabaseName, ReportsContainerName).Returns(container);

        var envelopeIterator = Substitute.For<FeedIterator<ReportEnvelopeDto>>();
        envelopeIterator.HasMoreResults.Returns(true, false);
        var envelopePage = Substitute.For<FeedResponse<ReportEnvelopeDto>>();
        envelopePage.GetEnumerator().Returns(_ => new List<ReportEnvelopeDto>
        {
            new() { DocType = "report_header" },
            new() { DocType = "report_candidate" }
        }.GetEnumerator());
        envelopeIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(envelopePage);

        container.GetItemQueryIterator<ReportEnvelopeDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(envelopeIterator);

        var headerIterator = Substitute.For<FeedIterator<ReportHeaderDto>>();
        headerIterator.HasMoreResults.Returns(true, false);
        var headerPage = Substitute.For<FeedResponse<ReportHeaderDto>>();
        headerPage.GetEnumerator().Returns(_ => new List<ReportHeaderDto>
        {
            new()
            {
                Id = "report-prov-001",
                BatchId = "batch-prov-456",
                DocumentId = "doc-prov-abc",
                Engine = "harvesting",
                DocType = "report_header",
                GeneratedAt = "2026-07-13T14:00:00Z",
                CandidateCount = 1
            }
        }.GetEnumerator());
        headerIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(headerPage);

        container.GetItemQueryIterator<ReportHeaderDto>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(headerIterator);

        var candidateIterator = Substitute.For<FeedIterator<ReportCandidateDto>>();
        candidateIterator.HasMoreResults.Returns(true, false);
        var candidatePage = Substitute.For<FeedResponse<ReportCandidateDto>>();
        candidatePage.GetEnumerator().Returns(_ => new List<ReportCandidateDto>
        {
            new()
            {
                CandidateId = "cand-prov-001",
                Title = "Candidate With Full Provenance",
                Description = "Has all US122-125 geometry fields",
                Rank = 1,
                WeightedScore = 88.0,
                ProvenanceLinks = new List<ProvenanceLinkDto>
                {
                    new()
                    {
                        DocumentId = "doc-prov-abc",
                        Locator = "page:3;span:200-500",
                        SourceKind = "pdf",
                        PageNumber = 3,
                        PreviewKind = "pdf",
                        PageDimensions = new List<PageDimensionDto>
                        {
                            new() { PageNumber = 1, Width = 595, Height = 842 },
                            new() { PageNumber = 3, Width = 595, Height = 842 }
                        },
                        HighlightRects = new List<HighlightRectDto>
                        {
                            new() { PageNumber = 3, X0 = 0.15, X1 = 0.85, Top = 0.25, Bottom = 0.35 },
                            new() { PageNumber = 3, X0 = 0.15, X1 = 0.5, Top = 0.40, Bottom = 0.45 }
                        },
                        CleanExcerpt = "This is the clean excerpt for viewer display",
                        Excerpt = "Original raw excerpt"
                    },
                    new()
                    {
                        DocumentId = "doc-prov-abc",
                        Locator = "lines:50-75",
                        SourceKind = "code",
                        PreviewKind = "code",
                        FilePath = "src/engine.py",
                        LineRange = new LineRangeDto { StartLine = 50, EndLine = 75 },
                        CleanExcerpt = "def process_batch():\n    # implementation\n    pass"
                    }
                }
            }
        }.GetEnumerator());
        candidateIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(candidatePage);

        container.GetItemQueryIterator<ReportCandidateDto>(
            Arg.Any<QueryDefinition>(), default(string), Arg.Any<QueryRequestOptions>()).Returns(candidateIterator);

        var result = await repository.GetHarvestingResultAsync("batch-prov-456", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Candidates.Should().HaveCount(1);

        var candidate = result.Candidates[0];
        candidate.ProvenanceLinks.Should().HaveCount(2);

        var pdfProvenance = candidate.ProvenanceLinks[0];
        pdfProvenance.PreviewKind.Should().Be("pdf");
        pdfProvenance.PageNumber.Should().Be(3);
        pdfProvenance.PageDimensions.Should().HaveCount(2);
        pdfProvenance.PageDimensions![0].PageNumber.Should().Be(1);
        pdfProvenance.PageDimensions![0].Width.Should().Be(595);
        pdfProvenance.PageDimensions![0].Height.Should().Be(842);
        pdfProvenance.HighlightRects.Should().HaveCount(2);
        pdfProvenance.HighlightRects![0].PageNumber.Should().Be(3);
        pdfProvenance.HighlightRects![0].X0.Should().Be(0.15);
        pdfProvenance.HighlightRects![0].X1.Should().Be(0.85);
        pdfProvenance.HighlightRects![0].Top.Should().Be(0.25);
        pdfProvenance.HighlightRects![0].Bottom.Should().Be(0.35);
        pdfProvenance.CleanExcerpt.Should().Be("This is the clean excerpt for viewer display");
        pdfProvenance.Excerpt.Should().Be("Original raw excerpt");

        var codeProvenance = candidate.ProvenanceLinks[1];
        codeProvenance.PreviewKind.Should().Be("code");
        codeProvenance.FilePath.Should().Be("src/engine.py");
        codeProvenance.LineRange.Should().NotBeNull();
        codeProvenance.LineRange!.StartLine.Should().Be(50);
        codeProvenance.LineRange!.EndLine.Should().Be(75);
        codeProvenance.CleanExcerpt.Should().Be("def process_batch():\n    # implementation\n    pass");
    }

    [Fact]
    public async Task GetCandidateDetailAsync_ReadsSplitCandidateDirectly_ByDeterministicId()
    {
        var repository = BuildRepository(out var reportsContainer, out var client);
        client.GetContainer(DatabaseName, ReportsContainerName).Returns(reportsContainer);

        var candidatesContainer = Substitute.For<Container>();
        var verdictsContainer = Substitute.For<Container>();
        client.GetContainer(DatabaseName, "candidates").Returns(candidatesContainer);
        client.GetContainer(DatabaseName, "verdicts").Returns(verdictsContainer);

        var candidateResponse = Substitute.For<ItemResponse<ReportCandidateDto>>();
        candidateResponse.Resource.Returns(new ReportCandidateDto
        {
            CandidateId = "cand-direct-001",
            Title = "Direct Read Candidate",
            Description = "Read via deterministic ID",
            Rank = 1,
            WeightedScore = 82.0,
            Axes = new Dictionary<string, HarvestingAxisScoreDto>
            {
                ["Novelty"] = new() { Axis = "Novelty", Score = 85, Refs = ["ref-1"] }
            },
            Citations = new List<CitationDto>
            {
                new() { Ref = "ref-1", SourceType = "patent_api", Title = "Patent Z", Url = "https://example.com/z", Similarity = 0.88 }
            },
            ProvenanceLinks = new List<ProvenanceLinkDto>
            {
                new()
                {
                    DocumentId = "doc-direct-xyz",
                    Locator = "page:2",
                    SourceKind = "pdf",
                    PageNumber = 2,
                    CleanExcerpt = "Direct read excerpt"
                }
            }
        });

        reportsContainer.ReadItemAsync<ReportCandidateDto>(
            "report_candidate:batch-direct-789:cand-direct-001",
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(candidateResponse);

        verdictsContainer.ReadItemAsync<VerdictResultDto>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<VerdictResultDto>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        candidatesContainer.ReadItemAsync<CandidateDocument>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ItemResponse<CandidateDocument>>(
                new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0)));

        var result = await repository.GetCandidateDetailAsync("batch-direct-789", "cand-direct-001", CancellationToken.None);

        result.Should().NotBeNull();
        result!.CandidateId.Should().Be("cand-direct-001");
        result.Title.Should().Be("Direct Read Candidate");
        result.WeightedScore.Should().Be(82.0);
        result.ProvenanceLinks.Should().HaveCount(1);
        result.ProvenanceLinks[0].CleanExcerpt.Should().Be("Direct read excerpt");
    }

    [Fact]
    public async Task GetSeedingResultAsync_Unchanged_StillReadsSingleDoc()
    {
        var repository = BuildRepository(out var container);

        var seedingDoc = new SeedingReportRecord
        {
            Id = "seeding-report-001",
            BatchId = "batch-seeding-999",
            Engine = "seeding",
            Opportunities = new List<SeedingOpportunityDto>
            {
                new() { Id = "opp-1", Title = "Seeding Opportunity", Description = "Still single doc", ConfidenceScore = 0.87 }
            },
            CreatedAt = "2026-07-13T16:00:00Z"
        };

        var iterator = Substitute.For<FeedIterator<SeedingReportRecord>>();
        iterator.HasMoreResults.Returns(true, false);
        var page = Substitute.For<FeedResponse<SeedingReportRecord>>();
        page.GetEnumerator().Returns(_ => new List<SeedingReportRecord> { seedingDoc }.GetEnumerator());
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(page);

        container.GetItemQueryIterator<SeedingReportRecord>(
            Arg.Any<QueryDefinition>(),
            Arg.Any<string>(),
            Arg.Any<QueryRequestOptions>()).Returns(iterator);

        var result = await repository.GetSeedingResultAsync("batch-seeding-999", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("seeding-report-001");
        result.Engine.Should().Be("seeding");
        result.Opportunities.Should().HaveCount(1);
        result.Opportunities[0].Title.Should().Be("Seeding Opportunity");
    }
}
