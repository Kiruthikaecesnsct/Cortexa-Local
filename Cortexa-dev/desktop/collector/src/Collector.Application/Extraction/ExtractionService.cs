using System.Security.Cryptography;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Extraction;

public sealed class ExtractionService(
    IPdfTextExtractor pdfTextExtractor,
    IDocxTextExtractor docxTextExtractor,
    ITokenCounter tokenCounter,
    IDocumentStore documentStore,
    IUnitStore unitStore,
    FileContentGuard fileContentGuard,
    TextNormalizer textNormalizer,
    CodeSplitter codeSplitter,
    UnitBuilder unitBuilder,
    DocumentStatusRules statusRules,
    ILogger<ExtractionService> logger)
{
    public async Task<IReadOnlyList<ExtractionResult>> ExtractAsync(
        IReadOnlyList<string> filePaths,
        SourceType sourceType,
        SourceKind sourceKind,
        CancellationToken cancellationToken)
    {
        var results = new List<ExtractionResult>(filePaths.Count);
        foreach (var filePath in filePaths)
        {
            results.Add(await ExtractOneAsync(filePath, sourceType, sourceKind, cancellationToken));
        }

        return results;
    }

    private async Task<ExtractionResult> ExtractOneAsync(
        string filePath,
        SourceType sourceType,
        SourceKind sourceKind,
        CancellationToken cancellationToken)
    {
        var document = await RegisterDocumentAsync(filePath, sourceType, sourceKind, cancellationToken);
        try
        {
            var outcome = await ParseIntoDraftsAsync(filePath, cancellationToken);
            if (outcome.SkipReason is { } reason)
            {
                var skipStatus = statusRules.ForSkip(reason);
                await documentStore.UpdateStatusAsync(document.Id, skipStatus, cancellationToken);
                return new ExtractionResult { SourcePath = filePath, DocumentId = document.Id, Status = skipStatus, Reason = reason.ToString() };
            }

            var units = BuildExtractionUnits(document.Id, outcome.Drafts!);
            await unitStore.InsertAsync(document.Id, units, cancellationToken);
            await documentStore.UpdateStatusAsync(document.Id, DocumentStatus.Extracted, cancellationToken);
            return new ExtractionResult { SourcePath = filePath, DocumentId = document.Id, Status = DocumentStatus.Extracted };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Extraction failed for {FilePath}.", filePath);
            await documentStore.UpdateStatusAsync(document.Id, DocumentStatus.Failed, cancellationToken);
            return new ExtractionResult { SourcePath = filePath, DocumentId = document.Id, Status = DocumentStatus.Failed, Reason = ex.Message };
        }
    }

    private async Task<Domain.Documents.CollectorDocument> RegisterDocumentAsync(
        string filePath,
        SourceType sourceType,
        SourceKind sourceKind,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        var contentHash = await ComputeHashAsync(filePath, cancellationToken);
        var document = await documentStore.UpsertAsync(
            sourceType,
            sourceKind,
            filePath,
            info.Name,
            contentHash,
            info.Length,
            cancellationToken);
        await documentStore.UpdateStatusAsync(document.Id, DocumentStatus.Extracting, cancellationToken);
        return document;
    }

    private async Task<ParseOutcome> ParseIntoDraftsAsync(string filePath, CancellationToken cancellationToken)
    {
        return FileClassifier.Classify(filePath) switch
        {
            FileClassification.Pdf => ParseOutcome.Ready(await ParsePdfAsync(filePath, cancellationToken)),
            FileClassification.Docx => ParseOutcome.Ready(await ParseDocxAsync(filePath, cancellationToken)),
            FileClassification.Code => await ParseCodeAsync(filePath, cancellationToken),
            _ => await ParseTextAsync(filePath, cancellationToken),
        };
    }

    private async Task<IReadOnlyList<UnitDraft>> ParsePdfAsync(string filePath, CancellationToken cancellationToken)
    {
        var parsed = await pdfTextExtractor.ExtractAsync(filePath, cancellationToken);
        var drafts = unitBuilder.BuildFromPages(parsed);
        return drafts.Select(d => d with { Text = textNormalizer.Normalize(d.Text) }).ToList();
    }

    private async Task<IReadOnlyList<UnitDraft>> ParseDocxAsync(string filePath, CancellationToken cancellationToken)
    {
        var parsed = await docxTextExtractor.ExtractAsync(filePath, cancellationToken);
        var normalized = textNormalizer.Normalize(parsed.Text);
        return unitBuilder.BuildFromSections(normalized);
    }

    private async Task<ParseOutcome> ParseCodeAsync(string filePath, CancellationToken cancellationToken)
    {
        var guard = await fileContentGuard.EvaluateAsync(filePath, cancellationToken);
        if (!guard.IsAccepted)
        {
            return ParseOutcome.Skip(ToSkipReason(guard.Outcome));
        }

        var language = CodeLanguage.ForExtension(Path.GetExtension(filePath))
            ?? new CodeLanguageInfo { Name = "text", Strategy = CodeSplitStrategy.None };
        var codeUnits = codeSplitter.Split(guard.Text!, language);
        return ParseOutcome.Ready(unitBuilder.BuildFromCode(codeUnits, filePath));
    }

    private async Task<ParseOutcome> ParseTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var guard = await fileContentGuard.EvaluateAsync(filePath, cancellationToken);
        if (!guard.IsAccepted)
        {
            return ParseOutcome.Skip(ToSkipReason(guard.Outcome));
        }

        var normalized = textNormalizer.Normalize(guard.Text!);
        return ParseOutcome.Ready(unitBuilder.BuildFromSections(normalized));
    }

    private static SkipReason ToSkipReason(FileGuardOutcome outcome) => outcome switch
    {
        FileGuardOutcome.TooLarge => SkipReason.TooLarge,
        FileGuardOutcome.BinaryContent => SkipReason.BinaryContent,
        _ => SkipReason.UnsupportedFormat,
    };

    private sealed record ParseOutcome
    {
        public IReadOnlyList<UnitDraft>? Drafts { get; private init; }

        public SkipReason? SkipReason { get; private init; }

        public static ParseOutcome Ready(IReadOnlyList<UnitDraft> drafts) => new() { Drafts = drafts };

        public static ParseOutcome Skip(SkipReason reason) => new() { SkipReason = reason };
    }

    private List<ExtractionUnit> BuildExtractionUnits(string documentId, IReadOnlyList<UnitDraft> drafts)
    {
        var units = new List<ExtractionUnit>(drafts.Count);
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            units.Add(new ExtractionUnit
            {
                Id = Guid.NewGuid().ToString("n"),
                DocumentId = documentId,
                Ordinal = i,
                UnitKind = draft.UnitKind,
                PageNumber = draft.PageNumber,
                SectionTitle = draft.SectionTitle,
                FilePath = draft.FilePath,
                StartLine = draft.StartLine,
                EndLine = draft.EndLine,
                Text = draft.Text,
                TokenCount = tokenCounter.Count(draft.Text),
                Status = DocumentStatus.Extracted,
            });
        }

        return units;
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
