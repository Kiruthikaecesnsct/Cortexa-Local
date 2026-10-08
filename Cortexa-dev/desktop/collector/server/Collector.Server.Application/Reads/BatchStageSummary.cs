using Collector.Domain.History;

namespace Collector.Server.Application.Reads;

public sealed record BatchStageSummary(
    BatchStage Stage,
    int HarvestingCompleted,
    int HarvestingTotal,
    int SeedingCompleted,
    int SeedingTotal);
