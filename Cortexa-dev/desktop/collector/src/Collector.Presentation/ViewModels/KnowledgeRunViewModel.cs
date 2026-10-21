using Collector.Application.Auth;
// Knowledge extraction is disabled by product decision; restore this using when re-enabling it.
// using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public static class KnowledgeFocusKeys
{
    public const string Cancel = "CandidateCancel";
    public const string Banner = "Banner";
}

public sealed record KnowledgeRunNavigation(INavigationService Navigation, SettingsNavigator Settings);

public sealed partial class KnowledgeRunViewModel : FocusableViewModel
{
    private readonly ProcessPasses _passes;
    private readonly KnowledgeRunNavigation _navigation;
    private readonly ISessionState _session;
    private IReadOnlyList<string> _documentIds = [];

    public KnowledgeRunViewModel(
        ProcessPasses passes,
        KnowledgeRunNavigation navigation,
        ISessionState session,
        CandidateProgressViewModel candidateProgress)
    {
        _passes = passes;
        _navigation = navigation;
        _session = session;
        CandidateProgress = candidateProgress;
        CandidateProgress.CancelCommand = ExtractKnowledgeCancelCommand;
    }

    public CandidateProgressViewModel CandidateProgress { get; }

    public string? Model { get; set; }

    public bool HasProviderKey => Readiness == ProviderReadinessState.Ready;

    public bool CanExtract => _documentIds.Count > 0 && HasProviderKey && !IsParsing && !IsRunning;

    public string? ExtractHelp => IsRunning || CanExtract ? null : ResolveHelp();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExtractHelp))]
    public partial CollectorProvider Provider { get; set; } = CollectorProvider.Claude;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProviderKey), nameof(CanExtract), nameof(ExtractHelp))]
    [NotifyCanExecuteChangedFor(nameof(ExtractKnowledgeCommand))]
    public partial ProviderReadinessState Readiness { get; set; } = ProviderReadinessState.Checking;

    // Knowledge extraction is disabled by product decision; uncomment to re-enable.
    // public string ProgressText => ExtractionStrings.RunProgress(CompletedUnits, TotalUnits);
    //
    // public double ProgressMaximum => Math.Max(1, TotalUnits);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtract), nameof(ExtractHelp))]
    [NotifyCanExecuteChangedFor(nameof(ExtractKnowledgeCommand))]
    public partial bool IsParsing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtract), nameof(ExtractHelp))]
    [NotifyCanExecuteChangedFor(nameof(ExtractKnowledgeCommand))]
    public partial bool IsRunning { get; set; }

    // Knowledge extraction is disabled by product decision; uncomment to re-enable.
    // [ObservableProperty]
    // [NotifyPropertyChangedFor(nameof(ProgressText))]
    // public partial int CompletedUnits { get; set; }
    //
    // [ObservableProperty]
    // [NotifyPropertyChangedFor(nameof(ProgressText), nameof(ProgressMaximum))]
    // public partial int TotalUnits { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? Banner { get; set; }

    public void SetDocuments(IReadOnlyList<string> documentIds)
    {
        if (_documentIds.SequenceEqual(documentIds))
        {
            return;
        }

        _documentIds = documentIds;
        OnPropertyChanged(nameof(CanExtract));
        OnPropertyChanged(nameof(ExtractHelp));
        ExtractKnowledgeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanExtract), IncludeCancelCommand = true)]
    private async Task ExtractKnowledgeAsync(CancellationToken cancellationToken)
    {
        StartRun();
        try
        {
            // Knowledge pass disabled by product decision; uncomment the next four lines (and Decide below) to re-enable.
            // var progress = new SyncProgress<ExtractionProgress>(OnProgress);
            // var request = new KnowledgeRunRequest(_documentIds, Provider, Model);
            // var outcome = await _passes.Knowledge.RunAsync(request, progress, cancellationToken);
            await RunCandidatePassAsync(cancellationToken);
            Decide();
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void StartRun()
    {
        Banner = null;
        _passes.KnowledgeState.Clear();
        _passes.CandidateState.Clear();
        IsRunning = true;
        RequestFocus(KnowledgeFocusKeys.Cancel);
    }

    private void Decide()
    {
        var state = _passes.CandidateState;
        if (!state.HasOutput)
        {
            return;
        }

        Proceed(state.Result?.AllCandidates.Count > 0);
    }

    private void Proceed(bool hasCandidates)
    {
        if (_session.Current != SessionState.SignedIn)
        {
            ShowSignInBanner(hasCandidates);
            return;
        }

        if (_navigation.Navigation.CurrentScreen?.Key == ScreenKeys.Extract)
        {
            _navigation.Navigation.NavigateTo(ScreenKeys.Review);
        }
    }

    private void ShowSignInBanner(bool hasCandidates)
    {
        if (!hasCandidates)
        {
            return;
        }

        Banner = SignInBanner();
        RequestFocus(KnowledgeFocusKeys.Banner);
    }

    // Knowledge run progress and banners are disabled by product decision; uncomment with the knowledge call above to re-enable.
    // private void OnProgress(ExtractionProgress progress)
    // {
    //     TotalUnits = progress.TotalUnits;
    //     CompletedUnits = progress.CompletedUnits;
    // }
    //
    // private BannerViewModel BannerFor(KnowledgeRunOutcome outcome)
    // {
    //     if (outcome.ConfiguredKeyCount > 1 && outcome.Status is KnowledgeRunStatus.KeyRejected or KnowledgeRunStatus.QuotaExceeded)
    //     {
    //         return Dismissible(
    //             BannerSeverity.Error,
    //             ExtractionStrings.AllKeysFailedTitle,
    //             ExtractionStrings.AllKeysFailedMessage(outcome.ConfiguredKeyCount));
    //     }
    //
    //     return outcome.Status switch
    //     {
    //         KnowledgeRunStatus.Canceled => Dismissible(BannerSeverity.Info, ExtractionStrings.CanceledTitle, ExtractionStrings.CanceledMessage),
    //         KnowledgeRunStatus.KeyMissing => KeyMissingBanner(outcome.Provider),
    //         KnowledgeRunStatus.KeyRejected => Dismissible(BannerSeverity.Error, ExtractionStrings.KeyRejectedTitle, ExtractionStrings.KeyRejectedMessage),
    //         KnowledgeRunStatus.QuotaExceeded => Dismissible(BannerSeverity.Warning, ExtractionStrings.QuotaExceededTitle, ExtractionStrings.QuotaExceededMessage),
    //         KnowledgeRunStatus.NetworkFailed => Dismissible(BannerSeverity.Error, ExtractionStrings.NetworkFailedTitle, ExtractionStrings.NetworkFailedMessage),
    //         _ => Dismissible(BannerSeverity.Error, ExtractionStrings.RunFailedTitle, ExtractionStrings.RunFailedMessage),
    //     };
    // }

    private string? ResolveHelp()
    {
        if (Readiness == ProviderReadinessState.Checking)
        {
            return null;
        }

        if (!HasProviderKey)
        {
            return Provider == CollectorProvider.Bedrock
                ? ExtractionStrings.BedrockMissingHelp
                : ExtractionStrings.KeyMissingTitle(Provider.ToString());
        }

        return ExtractionStrings.ExtractDisabledHelp;
    }

    // Disabled with the knowledge pass; uncomment to re-enable.
    // private BannerViewModel KeyMissingBanner(CollectorProvider provider)
    // {
    //     var bedrock = provider == CollectorProvider.Bedrock;
    //     return new BannerViewModel(new BannerContent
    //     {
    //         Severity = BannerSeverity.Warning,
    //         Title = bedrock ? ExtractionStrings.BedrockMissingTitle : ExtractionStrings.KeyMissingTitle(provider.ToString()),
    //         Message = bedrock ? ExtractionStrings.BedrockMissingMessage : ExtractionStrings.KeyMissingMessage(provider.ToString()),
    //         ActionText = ExtractionStrings.OpenSettings,
    //         ActionCommand = new RelayCommand(() => _navigation.Settings.Open(SettingsSection.ProviderKeys)),
    //         DismissCommand = DismissCommand,
    //     });
    // }

    private BannerViewModel SignInBanner() => new(new BannerContent
    {
        Severity = BannerSeverity.Info,
        Title = ExtractionStrings.ReadySignInTitle,
        Message = ExtractionStrings.ReadySignInMessage,
        ActionText = ExtractionStrings.SignIn,
        ActionCommand = new RelayCommand(() => _navigation.Navigation.NavigateTo(ScreenKeys.SignIn)),
        DismissCommand = DismissCommand,
    });

    // Disabled with the knowledge pass; uncomment to re-enable.
    // private BannerViewModel Dismissible(BannerSeverity severity, string title, string message) => new(new BannerContent
    // {
    //     Severity = severity,
    //     Title = title,
    //     Message = message,
    //     DismissCommand = DismissCommand,
    // });

    [RelayCommand]
    private void Dismiss() => Banner = null;
}
