namespace Collector.Server.Application.Rows;

public sealed record BatchResultRows(
    IReadOnlyList<HarvestingReportCandidateRow> HarvestingCandidates,
    SeedingReportRow? SeedingReport);
