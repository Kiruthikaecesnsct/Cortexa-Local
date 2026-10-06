using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Domain.Upload;
using Collector.Server.Application.Upload;
using Collector.Server.Application.Upload.Validation;
using Microsoft.Extensions.Options;

namespace Collector.Server.Tests.Upload;

public class KnowledgeUploadValidatorTests
{
    private const string FirstItem = "documents[0].knowledge_items[0]";
    private const string SecretValue = "TOP-SECRET-VALUE";
    private const int TinyLimit = 5;
    private const int TextLimit = 20;
    private const int InvalidLine = 0;
    private const int StartLine = 20;
    private const int EarlierEndLine = 10;
    private const int UndefinedEnumValue = 99;

    [Fact]
    public void Validate_ExcerptAtLimit_Passes()
    {
        var item = UploadRequests.PaperSection() with { Excerpt = new string('e', UploadLimits.MaxExcerptLength) };

        var result = Validate(UploadRequests.WithItem(SourceKind.Paper, item));

        Assert.NotNull(result.Request);
    }

    [Fact]
    public void Validate_ExcerptOverLimit_FailsOnExcerptField()
    {
        var item = UploadRequests.PaperSection() with { Excerpt = new string('e', UploadLimits.MaxExcerptLength + 1) };

        var result = Validate(UploadRequests.WithItem(SourceKind.Paper, item));

        Assert.Equal([$"{FirstItem}.excerpt"], Fields(result));
    }

    [Theory]
    [InlineData(SourceKind.Paper, UnitKind.Page, true)]
    [InlineData(SourceKind.Paper, UnitKind.Section, true)]
    [InlineData(SourceKind.Paper, UnitKind.File, false)]
    [InlineData(SourceKind.Paper, UnitKind.Module, false)]
    [InlineData(SourceKind.Code, UnitKind.File, true)]
    [InlineData(SourceKind.Code, UnitKind.Page, false)]
    [InlineData(SourceKind.Code, UnitKind.Section, false)]
    [InlineData(SourceKind.Code, UnitKind.Module, false)]
    public void Validate_SourceKindAndUnitKind_AcceptsOnlyAllowedPairs(SourceKind sourceKind, UnitKind unitKind, bool allowed)
    {
        var item = UploadRequests.Item(KnowledgeKind.Logic, unitKind);

        var result = Validate(UploadRequests.WithItem(sourceKind, item));

        Assert.Equal(allowed, result.Request is not null);
        Assert.Equal(allowed, !Fields(result).Contains($"{FirstItem}.unit_kind"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PaperPageWithoutValidPageNumber_FailsOnPageNumber(int? pageNumber)
    {
        var item = UploadRequests.Item(KnowledgeKind.Logic, UnitKind.Page) with
        {
            Source = new KnowledgeSource { PageNumber = pageNumber }
        };

        var result = Validate(UploadRequests.WithItem(SourceKind.Paper, item));

        Assert.Equal([$"{FirstItem}.source.page_number"], Fields(result));
    }

    [Fact]
    public void Validate_PaperSectionWithoutSection_FailsOnSection()
    {
        var item = UploadRequests.PaperSection() with { Source = new KnowledgeSource { PageNumber = 1 } };

        var result = Validate(UploadRequests.WithItem(SourceKind.Paper, item));

        Assert.Equal([$"{FirstItem}.source.section"], Fields(result));
    }

    [Fact]
    public void Validate_NullSourceOnSectionItem_FailsOnSectionInsteadOfThrowing()
    {
        var item = UploadRequests.PaperSection() with { Source = null! };

        var result = Validate(UploadRequests.WithItem(SourceKind.Paper, item));

        Assert.Equal([$"{FirstItem}.source.section"], Fields(result));
    }

    [Fact]
    public void Validate_CodeFileWithoutFilePath_FailsOnFilePath()
    {
        var item = UploadRequests.CodeFile() with { Source = new KnowledgeSource { LineStart = 1, LineEnd = 2 } };

        var result = Validate(UploadRequests.WithItem(SourceKind.Code, item));

        Assert.Equal([$"{FirstItem}.source.file_path"], Fields(result));
    }

    [Fact]
    public void Validate_CodeFileWithInvertedLineRange_FailsOnLineEnd()
    {
        var item = UploadRequests.CodeFile() with
        {
            Source = new KnowledgeSource { FilePath = "a.py", LineStart = StartLine, LineEnd = EarlierEndLine }
        };

        var result = Validate(UploadRequests.WithItem(SourceKind.Code, item));

        Assert.Equal([$"{FirstItem}.source.line_end"], Fields(result));
    }

    [Fact]
    public void Validate_CodeFileWithNonPositiveLines_FailsOnBothLineFields()
    {
        var item = UploadRequests.CodeFile() with
        {
            Source = new KnowledgeSource { FilePath = "a.py", LineStart = InvalidLine, LineEnd = InvalidLine }
        };

        var result = Validate(UploadRequests.WithItem(SourceKind.Code, item));

        Assert.Equal([$"{FirstItem}.source.line_start", $"{FirstItem}.source.line_end"], Fields(result));
    }

    [Fact]
    public void Validate_KnowledgeKindLayer_FailsOnKindField()
    {
        var item = UploadRequests.Item(KnowledgeKind.Layer, UnitKind.Section);

        var result = Validate(UploadRequests.WithItem(SourceKind.Paper, item));

        Assert.Equal([$"{FirstItem}.kind"], Fields(result));
    }

    [Fact]
    public void Validate_UnitKindModule_FailsOnUnitKindField()
    {
        var item = UploadRequests.Item(KnowledgeKind.Method, UnitKind.Module);

        var result = Validate(UploadRequests.WithItem(SourceKind.Code, item));

        Assert.Equal([$"{FirstItem}.unit_kind"], Fields(result));
    }

    [Fact]
    public void Validate_DocumentWithZeroItems_IsDropped()
    {
        var empty = UploadRequests.Document(2, SourceKind.Code, "empty.py");
        var request = UploadRequests.Valid(UploadRequests.PaperDocument(), empty);

        var result = Validate(request);

        Assert.Equal([UploadRequests.ClientId(1).ToString()], result.Request!.Documents.Select(d => d.ClientDocumentId));
    }

    [Fact]
    public void Validate_AllDocumentsEmpty_FailsOnDocuments()
    {
        var request = UploadRequests.Valid(UploadRequests.Document(1, SourceKind.Paper, "empty.pdf"));

        var result = Validate(request);

        Assert.Equal(["documents"], Fields(result));
    }

    [Fact]
    public void Validate_NoDocuments_FailsOnDocuments()
    {
        var request = UploadRequests.Valid() with { Documents = [] };

        var result = Validate(request);

        Assert.Equal(["documents"], Fields(result));
    }

    [Fact]
    public void Validate_DuplicateClientDocumentId_FailsOnSecondDocument()
    {
        var second = UploadRequests.CodeDocument() with { ClientDocumentId = UploadRequests.ClientId(1).ToString() };
        var request = UploadRequests.Valid(UploadRequests.PaperDocument(), second);

        var result = Validate(request);

        Assert.Equal(["documents[1].client_document_id"], Fields(result));
    }

    [Fact]
    public void Validate_ClientDocumentIdNotAGuid_FailsOnClientDocumentId()
    {
        var document = UploadRequests.PaperDocument() with { ClientDocumentId = "not-a-guid" };

        var result = Validate(UploadRequests.Valid(document));

        Assert.Equal(["documents[0].client_document_id"], Fields(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u200B\"\u202E")]
    public void Validate_FilenameEmptyAfterSanitizing_FailsOnFilename(string filename)
    {
        var document = UploadRequests.PaperDocument() with { Filename = filename };

        var result = Validate(UploadRequests.Valid(document));

        Assert.Equal(["documents[0].filename"], Fields(result));
    }

    [Fact]
    public void Validate_MoreDocumentsThanLimit_FailsOnDocuments()
    {
        var request = UploadRequests.Valid(UploadRequests.PaperDocument(), UploadRequests.CodeDocument());

        var result = Validate(request, options => options.MaxDocuments = 1);

        Assert.Equal(["documents"], Fields(result));
    }

    [Fact]
    public void Validate_MoreItemsThanLimit_FailsOnKnowledgeItems()
    {
        var document = UploadRequests.PaperDocument(UploadRequests.PaperSection(), UploadRequests.PaperSection());

        var result = Validate(UploadRequests.Valid(document), options => options.MaxItemsPerDocument = 1);

        Assert.Equal(["documents[0].knowledge_items"], Fields(result));
    }

    [Fact]
    public void Validate_BatchNameOverLimit_FailsOnBatchName()
    {
        var request = UploadRequests.Valid() with { BatchName = new string('b', TinyLimit + 1) };

        var result = Validate(request, options => options.MaxBatchNameLength = TinyLimit);

        Assert.Equal(["batch_name"], Fields(result));
    }

    [Fact]
    public void Validate_BatchNameBlank_FailsOnBatchName()
    {
        var request = UploadRequests.Valid() with { BatchName = "  " };

        var result = Validate(request);

        Assert.Equal(["batch_name"], Fields(result));
    }

    [Fact]
    public void Validate_TitleSummaryAndDetailsOverLimit_FailsOnEachField()
    {
        var oversize = new string('x', TextLimit + 1);
        var item = UploadRequests.PaperSection() with { Title = oversize, Summary = oversize, Details = oversize };

        var result = Validate(
            UploadRequests.WithItem(SourceKind.Paper, item),
            options =>
            {
                options.MaxTitleLength = TextLimit;
                options.MaxSummaryLength = TextLimit;
                options.MaxDetailsLength = TextLimit;
            });

        Assert.Equal([$"{FirstItem}.title", $"{FirstItem}.summary", $"{FirstItem}.details"], Fields(result));
    }

    [Fact]
    public void Validate_TitleAndSummaryMissing_FailsOnBothFields()
    {
        var item = UploadRequests.PaperSection() with { Title = " ", Summary = string.Empty };

        var result = Validate(UploadRequests.WithItem(SourceKind.Paper, item));

        Assert.Equal([$"{FirstItem}.title", $"{FirstItem}.summary"], Fields(result));
    }

    [Fact]
    public void Validate_FilePathOverLimit_FailsOnFilePath()
    {
        var item = UploadRequests.CodeFile() with
        {
            Source = new KnowledgeSource { FilePath = new string('p', UploadLimits.MaxFilePathLength + 1) }
        };

        var result = Validate(UploadRequests.WithItem(SourceKind.Code, item));

        Assert.Equal([$"{FirstItem}.source.file_path"], Fields(result));
    }

    [Fact]
    public void Validate_CollectorMissing_FailsOnCollector()
    {
        var request = UploadRequests.Valid() with { Collector = null! };

        var result = Validate(request);

        Assert.Equal(["collector"], Fields(result));
    }

    [Fact]
    public void Validate_CollectorProviderUndefined_FailsOnProvider()
    {
        var collector = TestData.Collector() with { Provider = (CollectorProvider)UndefinedEnumValue };

        var result = Validate(UploadRequests.Valid() with { Collector = collector });

        Assert.Equal(["collector.provider"], Fields(result));
    }

    [Theory]
    [InlineData(SourceType.Local)]
    [InlineData(SourceType.Github)]
    [InlineData(SourceType.AzureDevops)]
    [InlineData(SourceType.Ssh)]
    [InlineData(SourceType.CortexaRepo)]
    public void Validate_DefinedSourceType_Passes(SourceType sourceType)
    {
        var document = UploadRequests.PaperDocument() with { SourceType = sourceType };

        var result = Validate(UploadRequests.Valid(document));

        Assert.NotNull(result.Request);
    }

    [Fact]
    public void Validate_UndefinedSourceType_FailsOnSourceType()
    {
        var document = UploadRequests.PaperDocument() with { SourceType = (SourceType)UndefinedEnumValue };

        var result = Validate(UploadRequests.Valid(document));

        Assert.Equal(["documents[0].source_type"], Fields(result));
    }

    [Fact]
    public void Validate_UndefinedSourceKind_FailsOnSourceKind()
    {
        var document = UploadRequests.PaperDocument() with { SourceKind = (SourceKind)UndefinedEnumValue };

        var result = Validate(UploadRequests.Valid(document));

        Assert.Contains("documents[0].source_kind", Fields(result));
    }

    [Fact]
    public void Validate_NullItemInList_FailsWithItemPath()
    {
        var document = UploadRequests.PaperDocument(UploadRequests.PaperSection(), null!);

        var result = Validate(UploadRequests.Valid(document));

        Assert.Equal(["documents[0].knowledge_items[1]"], Fields(result));
    }

    [Fact]
    public void Validate_ManyRuleViolations_ErrorsNeverEchoSubmittedValues()
    {
        var item = UploadRequests.Item(KnowledgeKind.Layer, UnitKind.Module) with
        {
            Title = SecretValue + new string('t', TinyLimit),
            Excerpt = SecretValue + new string('e', UploadLimits.MaxExcerptLength),
            Source = new KnowledgeSource { FilePath = SecretValue }
        };
        var document = UploadRequests.PaperDocument(item) with { ClientDocumentId = SecretValue, Filename = SecretValue };
        var request = UploadRequests.Valid(document) with { BatchName = SecretValue + new string('b', TinyLimit) };

        var result = Validate(request, options =>
        {
            options.MaxTitleLength = TinyLimit;
            options.MaxBatchNameLength = TinyLimit;
        });

        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain(SecretValue, error.Field, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretValue, error.Message, StringComparison.Ordinal);
        });
    }

    private static UploadValidationResult Validate(KnowledgeUploadRequest request, Action<UploadOptions>? configure = null) =>
        new KnowledgeUploadValidator(Options.Create(UploadTestOptions.Create(configure))).Validate(request);

    private static IReadOnlyList<string> Fields(UploadValidationResult result) => [.. result.Errors.Select(e => e.Field)];
}
