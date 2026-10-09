using Collector.Application.Auth;
using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public static class KnowledgeFocusKeys
{
    public const string Cancel = "Cancel";
    public const string Banner = "Banner";
}

public sealed record KnowledgeRunNavigation(INavigationService Navigation, SettingsNavigator Settings);

public sealed partial class KnowledgeRunViewModel : FocusableViewModel
{
    private readonly IKnowledgeRunner _runner;
    private readonly KnowledgeRunState _state;
    private readonly KnowledgeRunNavigation _navigation;
    private readonly ISessionState _session;
    private IReadOnlyList<string> _documentIds = [];

    public KnowledgeRunViewModel(
        IKnowledgeRunner runner,
        KnowledgeRunState state,
        KnowledgeRunNavigation navigation,
        ISessionState session)
    {
        _runner = runner;
        _state = state;
        _navigation = navigation;
        _session = session;
    }

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

    public string ProgressText => ExtractionStrings.RunProgress(CompletedUnits, TotalUnits);

    public double ProgressMaximum => Math.Max(1, TotalUnits);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtract), nameof(ExtractHelp))]
    [NotifyCanExecuteChangedFor(nameof(ExtractKnowledgeCommand))]
    public partial bool IsParsing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtract), nameof(ExtractHelp))]
    [NotifyCanExecuteChangedFor(nameof(ExtractKnowledgeCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial int CompletedUnits { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText), nameof(ProgressMaximum))]
    public partial int TotalUnits { get; set; }

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
            var progress = new SyncProgress<ExtractionProgress>(OnProgress);
            var request = new KnowledgeRunRequest(_documentIds, Provider, Model);
            var outcome = await _runner.RunAsync(request, progress, cancellationToken);
            Finish(outcome);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void StartRun()
    {
        Banner = null;
        CompletedUnits = 0;
        TotalUnits = 0;
        IsRunning = true;
        RequestFocus(KnowledgeFocusKeys.Cancel);
    }

    private void OnProgress(ExtractionProgress progress)
    {
        TotalUnits = progress.TotalUnits;
        CompletedUnits = progress.CompletedUnits;
    }

    private void Finish(KnowledgeRunOutcome outcome)
    {
        if (outcome.Status == KnowledgeRunStatus.Completed && outcome.Result is { } result)
        {
            Complete(result);
            return;
        }

        Banner = BannerFor(outcome);
        RequestFocus(KnowledgeFocusKeys.Banner);
    }

    private void Complete(ExtractionRunResult result)
    {
        _state.Set(result);
        if (_session.Current != SessionState.SignedIn)
        {
            Banner = SignInBanner();
            RequestFocus(KnowledgeFocusKeys.Banner);
            return;
        }

        if (_navigation.Navigation.CurrentScreen?.Key == ScreenKeys.Extract)
        {
            _navigation.Navigation.NavigateTo(ScreenKeys.Review);
        }
    }

    private BannerViewModel BannerFor(KnowledgeRunOutcome outcome) => outcome.Status switch
    {
        KnowledgeRunStatus.Canceled => Dismissible(BannerSeverity.Info, ExtractionStrings.CanceledTitle, ExtractionStrings.CanceledMessage),
        KnowledgeRunStatus.KeyMissing => KeyMissingBanner(outcome.Provider),
        KnowledgeRunStatus.KeyRejected => Dismissible(BannerSeverity.Error, ExtractionStrings.KeyRejectedTitle, ExtractionStrings.KeyRejectedMessage),
        KnowledgeRunStatus.QuotaExceeded => Dismissible(BannerSeverity.Warning, ExtractionStrings.QuotaExceededTitle, ExtractionStrings.QuotaExceededMessage),
        KnowledgeRunStatus.NetworkFailed => Dismissible(BannerSeverity.Error, ExtractionStrings.NetworkFailedTitle, ExtractionStrings.NetworkFailedMessage),
        _ => Dismissible(BannerSeverity.Error, ExtractionStrings.RunFailedTitle, ExtractionStrings.RunFailedMessage),
    };

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

    private BannerViewModel KeyMissingBanner(CollectorProvider provider)
    {
        var bedrock = provider == CollectorProvider.Bedrock;
        return new BannerViewModel(new BannerContent
        {
            Severity = BannerSeverity.Warning,
            Title = bedrock ? ExtractionStrings.BedrockMissingTitle : ExtractionStrings.KeyMissingTitle(provider.ToString()),
            Message = bedrock ? ExtractionStrings.BedrockMissingMessage : ExtractionStrings.KeyMissingMessage(provider.ToString()),
            ActionText = ExtractionStrings.OpenSettings,
            ActionCommand = new RelayCommand(() => _navigation.Settings.Open(SettingsSection.ProviderKeys)),
            DismissCommand = DismissCommand,
        });
    }

    private BannerViewModel SignInBanner() => new(new BannerContent
    {
        Severity = BannerSeverity.Info,
        Title = ExtractionStrings.ReadySignInTitle,
        Message = ExtractionStrings.ReadySignInMessage,
        ActionText = ExtractionStrings.SignIn,
        ActionCommand = new RelayCommand(() => _navigation.Navigation.NavigateTo(ScreenKeys.SignIn)),
        DismissCommand = DismissCommand,
    });

    private BannerViewModel Dismissible(BannerSeverity severity, string title, string message) => new(new BannerContent
    {
        Severity = severity,
        Title = title,
        Message = message,
        DismissCommand = DismissCommand,
    });

    [RelayCommand]
    private void Dismiss() => Banner = null;
}
