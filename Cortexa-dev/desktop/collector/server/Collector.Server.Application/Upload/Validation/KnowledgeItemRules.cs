using Collector.Domain.Enums;
using Collector.Domain.Knowledge;

namespace Collector.Server.Application.Upload.Validation;

public sealed class KnowledgeItemRules(UploadOptions options)
{
    public IEnumerable<UploadValidationError> Validate(SourceKind sourceKind, KnowledgeItem item, string path) =>
        KindErrors(item, path)
            .Concat(UnitKindErrors(sourceKind, item, path))
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

    private IEnumerable<UploadValidationError> TextErrors(KnowledgeItem item, string path) =>
        Text(item.Title, options.MaxTitleLength, true, $"{path}.title")
            .Concat(Text(item.Summary, options.MaxSummaryLength, true, $"{path}.summary"))
            .Concat(Text(item.Details, options.MaxDetailsLength, false, $"{path}.details"))
            .Concat(Text(item.Excerpt, UploadLimits.MaxExcerptLength, false, $"{path}.excerpt"));

    private static IEnumerable<UploadValidationError> Text(string? value, int maxLength, bool required, string field)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            yield return new UploadValidationError(field, "is required.");
        }
        else if (value is not null && value.Length > maxLength)
        {
            yield return new UploadValidationError(field, "exceeds the maximum length.");
        }
    }

    private IEnumerable<UploadValidationError> SourceErrors(KnowledgeItem item, string path)
    {
        var source = item.Source ?? new KnowledgeSource();
        var sourcePath = $"{path}.source";
        return item.UnitKind switch
        {
            UnitKind.Page => PageErrors(source, sourcePath),
            UnitKind.Section => SectionErrors(source, sourcePath),
            UnitKind.File => FileErrors(source, sourcePath),
            _ => []
        };
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
