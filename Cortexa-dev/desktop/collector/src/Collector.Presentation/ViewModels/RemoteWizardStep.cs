using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public enum RemoteWizardStep
{
    Connect = 1,
    Repository = 2,
    Branch = 3,
    Files = 4,
    Proceed = 5,
}

public enum WizardStepState
{
    Upcoming,
    Current,
    Done,
}

public sealed class WizardStepItemViewModel(RemoteWizardStep step, string title) : ObservableObject
{
    private WizardStepState _state = WizardStepState.Upcoming;

    public RemoteWizardStep Step { get; } = step;

    public int Number => (int)Step;

    public string Title { get; } = title;

    public WizardStepState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(IsCurrent));
                OnPropertyChanged(nameof(IsUpcoming));
            }
        }
    }

    public bool IsDone => State == WizardStepState.Done;

    public bool IsCurrent => State == WizardStepState.Current;

    public bool IsUpcoming => State == WizardStepState.Upcoming;

    public void Sync(RemoteWizardStep current)
    {
        State = Step < current ? WizardStepState.Done : Step == current ? WizardStepState.Current : WizardStepState.Upcoming;
    }
}
