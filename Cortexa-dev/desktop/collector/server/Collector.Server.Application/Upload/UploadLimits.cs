using Collector.Domain.Enums;

namespace Collector.Server.Application.Upload;

public static class UploadLimits
{
    public const int MaxExcerptLength = 400;
    public const int MaxFilenameLength = 256;
    public const int MaxFilePathLength = 1024;

    public const UnitKind LayerUnitKind = UnitKind.Module;

    private static readonly IReadOnlyDictionary<SourceKind, UnitKind[]> UnitKindsBySource =
        new Dictionary<SourceKind, UnitKind[]>
        {
            [SourceKind.Paper] = [UnitKind.Page, UnitKind.Section],
            [SourceKind.Code] = [UnitKind.File, UnitKind.Module]
        };

    private static readonly KnowledgeKind[] AllowedKinds =
    [
        KnowledgeKind.Logic,
        KnowledgeKind.Algorithm,
        KnowledgeKind.Method,
        KnowledgeKind.DataModel,
        KnowledgeKind.Interface,
        KnowledgeKind.Workflow,
        KnowledgeKind.KeyContent,
        KnowledgeKind.Layer
    ];

    public static bool IsAllowedKind(KnowledgeKind kind) => AllowedKinds.Contains(kind);

    public static bool IsDocumentLevel(KnowledgeKind kind) => kind == KnowledgeKind.Layer;

    public static bool IsKnownSourceKind(SourceKind sourceKind) => UnitKindsBySource.ContainsKey(sourceKind);

    public static bool UnitKindFits(SourceKind sourceKind, UnitKind unitKind) =>
        UnitKindsBySource.TryGetValue(sourceKind, out var allowed) && allowed.Contains(unitKind);
}
