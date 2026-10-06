using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Domain.Upload;
using Microsoft.Extensions.Options;

namespace Collector.Server.Application.Upload.Validation;

public sealed class KnowledgeUploadValidator
{
    private readonly UploadOptions _options;
    private readonly KnowledgeItemRules _itemRules;

    public KnowledgeUploadValidator(IOptions<UploadOptions> options)
    {
        _options = options.Value;
        _itemRules = new KnowledgeItemRules(_options);
    }

    public UploadValidationResult Validate(KnowledgeUploadRequest request)
    {
        var errors = new List<UploadValidationError>();
        ValidateEnvelope(request, errors);
        var kept = ValidateDocuments(request.Documents ?? [], errors);

        if (errors.Count == 0 && kept.Count == 0)
        {
            errors.Add(new UploadValidationError("documents", "at least one document must contain knowledge items."));
        }

        return errors.Count == 0
            ? UploadValidationResult.Valid(request with { Documents = kept })
            : UploadValidationResult.Invalid(errors);
    }

    private void ValidateEnvelope(KnowledgeUploadRequest request, List<UploadValidationError> errors)
    {
        AddText(request.BatchName, _options.MaxBatchNameLength, "batch_name", errors);

        if (request.Collector is null)
        {
            errors.Add(new UploadValidationError("collector", "is required."));
            return;
        }

        AddText(request.Collector.AppVersion, _options.MaxTitleLength, "collector.app_version", errors);
        AddText(request.Collector.Model, _options.MaxTitleLength, "collector.model", errors);
        AddText(request.Collector.PromptVersion, _options.MaxTitleLength, "collector.prompt_version", errors);

        if (!Enum.IsDefined(request.Collector.Provider))
        {
            errors.Add(new UploadValidationError("collector.provider", "is not supported."));
        }
    }

    private List<UploadDocument> ValidateDocuments(
        IReadOnlyList<UploadDocument> documents,
        List<UploadValidationError> errors)
    {
        var kept = new List<UploadDocument>();

        if (documents.Count > _options.MaxDocuments)
        {
            errors.Add(new UploadValidationError("documents", "exceeds the maximum document count."));
            return kept;
        }

        var seenIds = new HashSet<Guid>();
        for (var index = 0; index < documents.Count; index++)
        {
            var normalized = ValidateDocument(documents[index], $"documents[{index}]", seenIds, errors);
            if (normalized is not null)
            {
                kept.Add(normalized);
            }
        }

        return kept;
    }

    private UploadDocument? ValidateDocument(
        UploadDocument? document,
        string path,
        HashSet<Guid> seenIds,
        List<UploadValidationError> errors)
    {
        if (document is null)
        {
            errors.Add(new UploadValidationError(path, "must not be null."));
            return null;
        }

        ValidateIdentity(document, path, seenIds, errors);
        ValidateSource(document, path, errors);
        var items = document.KnowledgeItems ?? [];
        ValidateItems(document.SourceKind, items, path, errors);
        return items.Count == 0 ? null : document with { KnowledgeItems = [.. items.OfType<KnowledgeItem>().Select(NormalizeItem)] };
    }

    private static void ValidateIdentity(
        UploadDocument document,
        string path,
        HashSet<Guid> seenIds,
        List<UploadValidationError> errors)
    {
        if (!Guid.TryParse(document.ClientDocumentId, out var clientId))
        {
            errors.Add(new UploadValidationError($"{path}.client_document_id", "must be a GUID."));
        }
        else if (!seenIds.Add(clientId))
        {
            errors.Add(new UploadValidationError($"{path}.client_document_id", "must be unique within the batch."));
        }

        if (!FilenameSanitizer.TryClean(document.Filename, out _))
        {
            errors.Add(new UploadValidationError($"{path}.filename", "is required."));
        }
    }

    private static void ValidateSource(UploadDocument document, string path, List<UploadValidationError> errors)
    {
        if (!UploadLimits.IsKnownSourceKind(document.SourceKind))
        {
            errors.Add(new UploadValidationError($"{path}.source_kind", "is not supported."));
        }

        if (!Enum.IsDefined(document.SourceType))
        {
            errors.Add(new UploadValidationError($"{path}.source_type", "is not supported."));
        }
    }

    private void ValidateItems(
        SourceKind sourceKind,
        IReadOnlyList<KnowledgeItem> items,
        string path,
        List<UploadValidationError> errors)
    {
        if (items.Count > _options.MaxItemsPerDocument)
        {
            errors.Add(new UploadValidationError($"{path}.knowledge_items", "exceeds the maximum item count."));
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            errors.AddRange(ValidateItem(sourceKind, items[index], $"{path}.knowledge_items[{index}]"));
        }
    }

    private IEnumerable<UploadValidationError> ValidateItem(SourceKind sourceKind, KnowledgeItem? item, string path) =>
        item is null
            ? [new UploadValidationError(path, "must not be null.")]
            : _itemRules.Validate(sourceKind, item, path);

    private static KnowledgeItem NormalizeItem(KnowledgeItem item) =>
        item.Source is null ? item with { Source = new KnowledgeSource() } : item;

    private static void AddText(string? value, int maxLength, string field, List<UploadValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new UploadValidationError(field, "is required."));
        }
        else if (value.Length > maxLength)
        {
            errors.Add(new UploadValidationError(field, "exceeds the maximum length."));
        }
    }
}
