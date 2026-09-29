using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class ProvenanceLinkDtoWireContractTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void ProvenanceLinkDto_SerializesWithFrozenSnakeCaseFieldNames()
    {
        var dto = new ProvenanceLinkDto
        {
            DocumentId = "doc-1",
            Locator = "chars:10-20",
            SourceKind = "pdf",
            HitUrl = "https://example.test/doc",
            ChunkId = "chunk-1",
            SourceChunkIndex = 2,
            PageNumber = 3,
            SectionHint = "Section 2.1",
            SpanStart = 10,
            SpanEnd = 20,
            Excerpt = "raw span text",
            PreviewKind = "pdf",
            PageDimensions = [new PageDimensionDto { PageNumber = 1, Width = 612.0, Height = 792.0 }],
            HighlightRects = [new HighlightRectDto { PageNumber = 1, X0 = 10.5, X1 = 200.5, Top = 50.0, Bottom = 65.0 }],
            CleanExcerpt = "A clean, sentence-bounded excerpt.",
            FilePath = "src/module.py",
            LineRange = new LineRangeDto { StartLine = 10, EndLine = 20 }
        };

        var json = JsonSerializer.Serialize(dto, Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var expectedTopLevelFields = new[]
        {
            "document_id", "locator", "source_kind", "hit_url", "chunk_id", "source_chunk_index",
            "page_number", "section_hint", "span_start", "span_end", "excerpt", "preview_kind",
            "page_dimensions", "highlight_rects", "clean_excerpt", "file_path", "line_range"
        };

        foreach (var field in expectedTopLevelFields)
            root.TryGetProperty(field, out _).Should().BeTrue($"field '{field}' must be present on the wire");

        var pageDimension = root.GetProperty("page_dimensions")[0];
        pageDimension.GetProperty("page_number").GetInt32().Should().Be(1);
        pageDimension.GetProperty("width").GetDouble().Should().Be(612.0);
        pageDimension.GetProperty("height").GetDouble().Should().Be(792.0);

        var highlightRect = root.GetProperty("highlight_rects")[0];
        highlightRect.GetProperty("page_number").GetInt32().Should().Be(1);
        highlightRect.GetProperty("x0").GetDouble().Should().Be(10.5);
        highlightRect.GetProperty("x1").GetDouble().Should().Be(200.5);
        highlightRect.GetProperty("top").GetDouble().Should().Be(50.0);
        highlightRect.GetProperty("bottom").GetDouble().Should().Be(65.0);

        var lineRange = root.GetProperty("line_range");
        lineRange.GetProperty("start_line").GetInt32().Should().Be(10);
        lineRange.GetProperty("end_line").GetInt32().Should().Be(20);
    }

    [Fact]
    public void ProvenanceLinkDto_LegacyPayloadWithoutNewFields_StillDeserializes()
    {
        const string json = """
        {
            "document_id": "doc-legacy",
            "locator": "chars:0-5",
            "source_kind": "pdf",
            "excerpt": "legacy excerpt"
        }
        """;

        var dto = JsonSerializer.Deserialize<ProvenanceLinkDto>(json, Options);

        dto.Should().NotBeNull();
        dto!.DocumentId.Should().Be("doc-legacy");
        dto.PreviewKind.Should().BeNull();
        dto.PageDimensions.Should().BeNull();
        dto.HighlightRects.Should().BeNull();
        dto.CleanExcerpt.Should().BeNull();
        dto.FilePath.Should().BeNull();
        dto.LineRange.Should().BeNull();
    }
}
