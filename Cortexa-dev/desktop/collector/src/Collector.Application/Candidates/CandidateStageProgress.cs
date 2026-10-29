namespace Collector.Application.Candidates;

public sealed class CandidateStageProgress
{
    private readonly CandidateStageWeights _weights;
    private readonly double[] _fractions;
    private readonly object[] _locks;

    public CandidateStageProgress(CandidateStageWeights weights, int documentCount)
    {
        _weights = weights;
        var count = Math.Max(documentCount, 0);
        _fractions = new double[count];
        _locks = new object[count];
        for (var index = 0; index < count; index++)
        {
            _locks[index] = new object();
        }
    }

    public int DocumentCount => _fractions.Length;

    public double OverallFraction
    {
        get
        {
            if (_fractions.Length == 0)
            {
                return 0d;
            }

            var sum = 0d;
            for (var index = 0; index < _fractions.Length; index++)
            {
                sum += Volatile.Read(ref _fractions[index]);
            }

            return Math.Clamp(sum / _fractions.Length, 0d, 1d);
        }
    }

    public double Report(int documentIndex, CandidateStage stage, double stageFraction)
    {
        if (documentIndex < 0 || documentIndex >= _fractions.Length)
        {
            return OverallFraction;
        }

        var clampedStageFraction = Math.Clamp(stageFraction, 0d, 1d);
        var fraction = Math.Clamp(StageStart(stage) + (WeightOf(stage) * clampedStageFraction), 0d, 1d);
        lock (_locks[documentIndex])
        {
            if (fraction > _fractions[documentIndex])
            {
                _fractions[documentIndex] = fraction;
            }
        }

        return OverallFraction;
    }

    public double FractionOf(int documentIndex)
    {
        if (documentIndex < 0 || documentIndex >= _fractions.Length)
        {
            return 0d;
        }

        lock (_locks[documentIndex])
        {
            return _fractions[documentIndex];
        }
    }

    private double StageStart(CandidateStage stage) => stage switch
    {
        CandidateStage.UploadRead => 0d,
        CandidateStage.Extraction => _weights.UploadRead,
        CandidateStage.LlmCall => _weights.UploadRead + _weights.Extraction,
        CandidateStage.Save => _weights.UploadRead + _weights.Extraction + _weights.LlmCall,
        _ => 0d,
    };

    private double WeightOf(CandidateStage stage) => stage switch
    {
        CandidateStage.UploadRead => _weights.UploadRead,
        CandidateStage.Extraction => _weights.Extraction,
        CandidateStage.LlmCall => _weights.LlmCall,
        CandidateStage.Save => _weights.Save,
        _ => 0d,
    };
}
