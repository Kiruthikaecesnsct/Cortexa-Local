namespace Collector.Application.Candidates;

public sealed class CandidateStageWeights
{
    public const string SectionName = "CandidateStageWeights";

    public double UploadRead { get; set; } = 0.1;

    public double Extraction { get; set; } = 0.2;

    public double LlmCall { get; set; } = 0.6;

    public double Save { get; set; } = 0.1;
}
