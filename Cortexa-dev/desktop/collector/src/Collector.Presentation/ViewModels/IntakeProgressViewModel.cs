using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class IntakeProgressViewModel(TimeProvider time) : ObservableObject, IDisposable
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private ITimer? _timer;
    private IntakeState _state = IntakeState.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsActive { get; private set; }

    [ObservableProperty]
    public partial string FetchedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SplitText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentFile { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial double ProgressMaximum { get; private set; } = 1;

    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; } = true;

    public bool HasFetchedText => FetchedText.Length > 0;

    public bool HasSplitText => SplitText.Length > 0;

    public bool HasCurrentFile => CurrentFile.Length > 0;

    public CancellationToken Token
    {
        get
        {
            lock (_gate)
            {
                return _cts?.Token ?? CancellationToken.None;
            }
        }
    }

    public CancellationToken Begin()
    {
        lock (_gate)
        {
            if (_cts is not null)
            {
                return _cts.Token;
            }

            _cts = new CancellationTokenSource();
            _state = IntakeState.Empty;
            _timer = time.CreateTimer(_ => OnTick(), null, IntakeTiming.FlushInterval, IntakeTiming.FlushInterval);
            IsActive = true;
            Render();
            return _cts.Token;
        }
    }

    public void ReportFetch(int processed, int total, string? currentPath, bool isPaused)
    {
        lock (_gate)
        {
            _state = _state with
            {
                Phase = IntakePhase.Fetch,
                FetchDone = processed,
                FetchTotal = total,
                IsPaused = isPaused,
                Current = currentPath ?? _state.Current,
                IsDirty = true,
            };
        }
    }

    public void ReportSplit(int done, int total, string? currentPath)
    {
        lock (_gate)
        {
            _state = _state with
            {
                Phase = IntakePhase.Split,
                SplitDone = done,
                SplitTotal = total,
                Current = currentPath ?? _state.Current,
                IsDirty = true,
            };
        }
    }

    public void End()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _cts?.Dispose();
            _cts = null;
            _state = IntakeState.Empty;
            IsActive = false;
            Render();
        }
    }

    public void Dispose() => End();

    private bool CanCancel() => IsActive;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        CancellationTokenSource? source;
        lock (_gate)
        {
            source = _cts;
        }

        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnTick()
    {
        lock (_gate)
        {
            if (!_state.IsDirty)
            {
                return;
            }
        }

        UiThread.Post(RenderIfActive);
    }

    private void RenderIfActive()
    {
        lock (_gate)
        {
            if (_cts is not null)
            {
                Render();
            }
        }
    }

    private void Render()
    {
        var state = _state;
        _state = state with { IsDirty = false };
        FetchedText = FormatFetched(state);
        SplitText = state.SplitTotal > 0 ? ExtractionStrings.IntakeSplit(state.SplitDone, state.SplitTotal) : string.Empty;
        CurrentFile = state.Current.Length > 0 ? ExtractionStrings.IntakeCurrent(state.Current) : string.Empty;
        var (done, total) = state.Phase == IntakePhase.Split
            ? (state.SplitDone, state.SplitTotal)
            : (state.FetchDone, state.FetchTotal);
        IsIndeterminate = total <= 0;
        ProgressMaximum = Math.Max(1, total);
        ProgressValue = done;
        OnPropertyChanged(nameof(HasFetchedText));
        OnPropertyChanged(nameof(HasSplitText));
        OnPropertyChanged(nameof(HasCurrentFile));
    }

    private static string FormatFetched(IntakeState state)
    {
        if (state.FetchTotal > 0)
        {
            return state.IsPaused
                ? ExtractionStrings.IntakePaused(state.FetchDone, state.FetchTotal)
                : ExtractionStrings.IntakeFetched(state.FetchDone, state.FetchTotal);
        }

        return state.Phase == IntakePhase.Fetch ? ExtractionStrings.IntakeReadingTree : string.Empty;
    }

    private enum IntakePhase
    {
        None,
        Fetch,
        Split,
    }

    private readonly record struct IntakeState(
        IntakePhase Phase,
        int FetchDone,
        int FetchTotal,
        bool IsPaused,
        int SplitDone,
        int SplitTotal,
        string Current,
        bool IsDirty)
    {
        public static IntakeState Empty => new(IntakePhase.None, 0, 0, false, 0, 0, string.Empty, false);
    }
}
