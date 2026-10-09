using Collector.Domain.Extraction;

namespace Collector.Application.Extraction;

public sealed record ExtractedFile(ExtractionResult Result, IReadOnlyList<ExtractionUnit> Units);
