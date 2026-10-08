using Collector.Domain.History;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed partial class StageStepViewModel : ObservableObject
{
    public StageStepViewModel(BatchStage stage)
    {
        Stage = stage;
        Label = HistoryStrings.StageName(stage);
        CountText = HistoryStrings.StepNotStarted;
        AutomationName = HistoryStrings.StepAutomationName(Label, HistoryStrings.StatusName(Status), CountText);
    }

    public BatchStage Stage { get; }

    public string Label { get; }

    public bool HasConnector => Stage != BatchStage.Seeded;

    [ObservableProperty]
    public partial StepStatus Status { get; set; }

    [ObservableProperty]
    public partial string CountText { get; set; }

    [ObservableProperty]
    public partial string AutomationName { get; set; }

    public void Update(BatchSummary batch)
    {
        Status = BatchProgress.StatusOf(batch, (int)Stage);
        CountText = BatchProgress.CountText(batch, (int)Stage, Status);
        AutomationName = HistoryStrings.StepAutomationName(Label, HistoryStrings.StatusName(Status), CountText);
    }
}
