using Collector.Presentation.Services;

namespace Collector.Presentation.ViewModels;

public sealed record ProcessPasses(
    IKnowledgeRunner Knowledge,
    KnowledgeRunState KnowledgeState,
    ICandidateRunner Candidates,
    CandidateRunState CandidateState);
