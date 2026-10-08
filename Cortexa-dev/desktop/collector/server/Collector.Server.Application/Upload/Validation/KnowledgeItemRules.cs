using Collector.Domain.Enums;
using Collector.Domain.Knowledge;

namespace Collector.Server.Application.Upload.Validation;

public sealed class KnowledgeItemRules(UploadOptions options)
{
    private const string RequiredMessage = "is required.";
    private const string MaxLengthMessage = "exceeds the maximum length.";
    private const string FolderMessage = "must be a relative folder path ending with a slash.";
    private const string LayerLineMessage = "must be omitted for layer items.";
    private const string LayerUnitKindMessage = "layer requires unit_kind module.";

    private delegate IEnumerable<UploadValidationError> SourceRule(
        KnowledgeItemRules rules,
        KnowledgeSource source,
        string path);

    private static readonly IReadOnlyDictionary<UnitKind, SourceRule> SourceRules =
        new Dictionary<UnitKind, SourceRule>
        {
            [UnitKind.Page] = static (_, source, path) => PageErrors(source, path),
            [UnitKind.Section] = static (rules, source, path) => rules.SectionErrors(source, path),
            [UnitKind.File] = static (_, source, path) => FileErrors(source, path),
            [UnitKind.Module] = static (_, source, path) => FileErrors(source, path)
        };

    public IEnumerable<UploadValidationError> Validate(SourceKind sourceKind, KnowledgeItem item, string path) =>
        KindErrors(item, path)
            .Concat(UnitKindErrors(sourceKind, item, path))
            .Concat(LayerPairingErrors(item, path))
            .Concat(TextErrors(item, path))
            .Concat(SourceErrors(item, path));

    private static IEnumerable<UploadValidationError> KindErrors(KnowledgeItem item, string path)
    {
        if (!UploadLimits.IsAllowedKind(item.Kind))
        {
            yield return new UploadValidationError($"{path}.kind", "kind is not supported.");
        }
    }

    private static IEnumerable<UploadValidationError> UnitKindErrors(
        SourceKind sourceKind,
        KnowledgeItem item,
        string path)
    {
        if (!UploadLimits.UnitKindFits(sourceKind, item.UnitKind))
        {
            yield return new UploadValidationError($"{path}.unit_kind", "unit_kind is not allowed for this source_kind.");
        }
    }

    private static IEnumerable<UploadValidationError> LayerPairingErrors(KnowledgeItem item, string path)
    {
        if (UploadLimits.IsDocumentLevel(item.Kind) && item.UnitKind != UploadLimits.LayerUnitKind)
        {
            yield return new UploadValidationError($"{path}.unit_kind", LayerUnitKindMessage);
        }
    }

    private IEnumerable<UploadValidationError> TextErrors(KnowledgeItem item, string path) =>
        Text(item.Title, options.MaxTitleLength, true, $"{path}.title")
            .Concat(Text(item.Summary, options.MaxSummaryLength, true, $"{path}.summary"))
            .Concat(Text(item.Details, options.MaxDetailsLength, false, $"{path}.details"))
            .Concat(Text(item.Excerpt, UploadLimits.MaxExcerptLength, false, $"{path}.excerpt"));

    private static IEnumerable<UploadValidationError> Text(string? value, int maxLength, bool required, string field)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            yield return new UploadValidationError(field, RequiredMessage);
        }
        else if (value is not null && value.Length > maxLength)
        {
            yield return new UploadValidationError(field, MaxLengthMessage);
        }
    }

    private IEnumerable<UploadValidationError> SourceErrors(KnowledgeItem item, string path)
    {
        var source = item.Source ?? new KnowledgeSource();
        var sourcePath = $"{path}.source";
        if (UploadLimits.IsDocumentLevel(item.Kind))
        {
            return LayerErrors(source, sourcePath);
        }

        return SourceRules.TryGetValue(item.UnitKind, out var rule) ? rule(this, source, sourcePath) : [];
    }

    private static IEnumerable<UploadValidationError> LayerErrors(KnowledgeSource source, string path) =>
        LayerPathErrors(source.FilePath, path).Concat(LayerLineErrors(source, path));

    private static IEnumerable<UploadValidationError> LayerPathErrors(string? filePath, string path)
    {
        var field = $"{path}.file_path";
        if (string.IsNullOrWhiteSpace(filePath))
        {
            yield return new UploadValidationError(field, RequiredMessage);
        }
        else if (filePath.Length > UploadLimits.MaxFilePathLength)
        {
            yield return new UploadValidationError(field, MaxLengthMessage);
        }
        else if (!FolderPathRule.IsFolder(filePath))
        {
            yield return new UploadValidationError(field, FolderMessage);
        }
    }

    private static IEnumerable<UploadValidationError> LayerLineErrors(KnowledgeSource source, string path)
    {
        if (source.LineStart is not null)
        {
            yield return new UploadValidationError($"{path}.line_start", LayerLineMessage);
        }

        if (source.LineEnd is not null)
        {
            yield return new UploadValidationError($"{path}.line_end", LayerLineMessage);
        }
    }

    private static IEnumerable<UploadValidationError> PageErrors(KnowledgeSource source, string path)
    {
        if (source.PageNumber is null or < 1)
        {
            yield return new UploadValidationError($"{path}.page_number", "must be 1 or greater.");
        }
    }

    private IEnumerable<UploadValidationError> SectionErrors(KnowledgeSource source, string path) =>
        Text(source.Section, options.MaxTitleLength, true, $"{path}.section");

    private static IEnumerable<UploadValidationError> FileErrors(KnowledgeSource source, string path) =>
        Text(source.FilePath, UploadLimits.MaxFilePathLength, true, $"{path}.file_path")
            .Concat(LineErrors(source, path));

    private static IEnumerable<UploadValidationError> LineErrors(KnowledgeSource source, string path)
    {
        if (source.LineStart is < 1)
        {
            yield return new UploadValidationError($"{path}.line_start", "must be 1 or greater.");
        }

        if (source.LineEnd is < 1)
        {
            yield return new UploadValidationError($"{path}.line_end", "must be 1 or greater.");
        }
        else if (source.LineStart > source.LineEnd)
        {
            yield return new UploadValidationError($"{path}.line_end", "must not be less than line_start.");
        }
    }
}
