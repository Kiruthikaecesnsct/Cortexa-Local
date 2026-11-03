using Collector.Domain.Enums;

namespace Collector.Application.Candidates;

public sealed record CandidateRunContext(CollectorProvider Provider, string Model);
