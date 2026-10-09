using Collector.Application.Extraction;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class DocumentRowViewModelTests
{
    private const string LocalPath = @"C:\papers\thesis.pdf";
    private const string RepoPath = "docs/guides/setup.md";
    private const string CachePath = @"C:\cache\Github\octo\main\docs\guides\setup.md";
    private const string Origin = "octo/alpha @ main";
    private const int UnitCount = 7;
    private const int TokenCount = 420;
    private const int PromptTokenCount = 900;

    [Fact]
    public void Constructor_LocalFile_DerivesFilenameFromTheSourcePath()
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(LocalPath));

        Assert.Equal("thesis.pdf", row.Filename);
    }

    [Fact]
    public void Constructor_RepoPath_UsesRepoRelativeNameAndFolderInsteadOfTheCachePath()
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(CachePath, repoPath: RepoPath));

        Assert.Equal("setup.md", row.Filename);
        Assert.Equal("docs/guides", row.Folder);
    }

    [Fact]
    public void Constructor_RepoPathAtRoot_FolderIsTheRootLabel()
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(CachePath, repoPath: "README.md"));

        Assert.Equal(ExtractionStrings.FolderRoot, row.Folder);
    }

    [Fact]
    public void Constructor_LocalFileWithoutRepoPath_FolderIsTheFilesOwnDirectory()
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(LocalPath));

        Assert.Equal(@"C:\papers", row.Folder);
    }

    [Fact]
    public void Constructor_Outcome_CopiesCountsAndIdentity()
    {
        var outcome = SplitOutcomes.Make(LocalPath, documentId: "doc-1", units: UnitCount, tokens: TokenCount, promptTokens: PromptTokenCount);

        var row = new DocumentRowViewModel(outcome);

        Assert.Equal("doc-1", row.DocumentId);
        Assert.Equal(UnitCount, row.UnitCount);
        Assert.Equal(TokenCount, row.TokenCount);
        Assert.Equal(PromptTokenCount, row.PromptTokens);
    }

    [Fact]
    public void SourceCaption_WithOrigin_PrefixesTheOrigin()
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(CachePath, repoPath: RepoPath), Origin);

        Assert.Equal($"{Origin} · setup.md", row.SourceCaption);
    }

    [Fact]
    public void SourceCaption_WithoutOrigin_IsTheSourcePath()
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(LocalPath));

        Assert.Equal(LocalPath, row.SourceCaption);
    }

    [Theory]
    [InlineData(DocumentStatus.Pending, "Pending")]
    [InlineData(DocumentStatus.Extracting, "Extracting")]
    [InlineData(DocumentStatus.Extracted, "Analyzed")]
    [InlineData(DocumentStatus.Failed, "Failed")]
    [InlineData(DocumentStatus.Excluded, "Excluded")]
    public void StatusLabel_Status_MapsToAHumanReadableLabel(DocumentStatus status, string expected)
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(LocalPath, status));

        Assert.Equal(expected, row.StatusLabel);
    }

    [Theory]
    [InlineData(DocumentStatus.Pending, false)]
    [InlineData(DocumentStatus.Extracting, false)]
    [InlineData(DocumentStatus.Extracted, false)]
    [InlineData(DocumentStatus.Failed, true)]
    [InlineData(DocumentStatus.Excluded, true)]
    public void IsSkipped_Status_FlagsFailedAndExcludedDocuments(DocumentStatus status, bool expected)
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(LocalPath, status));

        Assert.Equal(expected, row.IsSkipped);
    }

    [Fact]
    public void AutomationName_AnalyzedDocument_ListsFilenameFolderCountsAndStatus()
    {
        var row = new DocumentRowViewModel(SplitOutcomes.Make(LocalPath, units: UnitCount, tokens: TokenCount));

        Assert.Equal(@"thesis.pdf, folder C:\papers, 7 units, 420 tokens, Analyzed", row.AutomationName);
    }

    [Theory]
    [InlineData(nameof(SkipReason.TooLarge), "File is too large to parse.")]
    [InlineData(nameof(SkipReason.BinaryContent), "File content could not be read as text.")]
    [InlineData(nameof(SkipReason.UnsupportedFormat), "This file type isn't supported.")]
    public void SkipReasonTextFor_ExcludedReason_MapsToFriendlyText(string reason, string expected)
    {
        Assert.Equal(expected, DocumentRowViewModel.SkipReasonTextFor(DocumentStatus.Excluded, reason));
    }

    [Fact]
    public void SkipReasonTextFor_FailedStatus_IsAGenericParsingFailureMessage()
    {
        Assert.Equal("Parsing failed.", DocumentRowViewModel.SkipReasonTextFor(DocumentStatus.Failed, "boom"));
    }

    [Fact]
    public void SkipReasonText_ExcludedOutcome_UsesTheOutcomeReason()
    {
        var outcome = SplitOutcomes.Make(LocalPath, DocumentStatus.Excluded, documentId: "doc-1", reason: nameof(SkipReason.TooLarge));

        var row = new DocumentRowViewModel(outcome);

        Assert.Equal("File is too large to parse.", row.SkipReasonText);
    }
}
