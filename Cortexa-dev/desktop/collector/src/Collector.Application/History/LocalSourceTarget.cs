using Collector.Domain.Enums;

namespace Collector.Application.History;

public sealed record LocalSourceTarget(string Path, SourceKind SourceKind);
