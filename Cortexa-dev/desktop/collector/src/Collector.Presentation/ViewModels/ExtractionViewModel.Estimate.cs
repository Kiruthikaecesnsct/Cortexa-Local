using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed partial class ExtractionViewModel
{
    public int TotalEstimatedTokens => PromptTokens + EstimatedOutputTokens;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalEstimatedTokens))]
    public partial int PromptTokens { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalEstimatedTokens))]
    public partial int EstimatedOutputTokens { get; set; }

    private void RecomputeEstimate()
    {
        var maxOutputTokens = _outputLimits.MaxOutputTokensFor(ActiveModel.Provider);
        var estimate = _tokenEstimator.FromPromptTokens(_tally.PromptTokens, maxOutputTokens);
        PromptTokens = estimate.PromptTokens;
        EstimatedOutputTokens = estimate.EstimatedOutputTokens;
    }
}
