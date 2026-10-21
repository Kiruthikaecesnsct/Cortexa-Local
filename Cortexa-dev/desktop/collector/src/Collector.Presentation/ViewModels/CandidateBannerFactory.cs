using System.Globalization;
using System.Windows.Input;
using Collector.Application.Candidates;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;

namespace Collector.Presentation.ViewModels;

public sealed record CandidateBannerActions(ICommand OpenSettings, ICommand DismissRun, ICommand DismissTooLarge);

public sealed record CandidateBanners(BannerViewModel? Run, BannerViewModel? TooLarge);

public static class CandidateBannerFactory
{
    private const int MaxTooLargeLines = 5;
    private const double BytesPerMegabyte = 1024d * 1024d;

    private sealed record Context(CandidateRunState State, IReadOnlyList<CandidateDocumentResult> Documents, CandidateBannerActions Actions)
    {
        public bool Has(CandidateOutcome outcome) => Documents.Any(document => document.Outcome == outcome);

        public int Count(CandidateOutcome outcome) => Documents.Count(document => document.Outcome == outcome);
    }

    private sealed record Rule(Func<Context, bool> Matches, Func<Context, BannerContent> Build, bool AppendsPartial = true);

    private static readonly Rule[] Rules =
    [
        new(context => context.Has(CandidateOutcome.KeyMissing), KeyMissing),
        new(IsAllKeysFailed, AllKeysFailed),
        new(context => context.Has(CandidateOutcome.KeyRejected), KeyRejected),
        new(context => context.Has(CandidateOutcome.Quota), Quota),
        new(context => context.Has(CandidateOutcome.Network), Network),
        new(context => context.State.Failed || context.Has(CandidateOutcome.Failed), Failed),
        new(context => context.State.Canceled || context.Has(CandidateOutcome.Canceled), Canceled, AppendsPartial: false),
    ];

    public static CandidateBanners Create(CandidateRunState state, CandidateBannerActions actions)
    {
        var context = new Context(state, state.Result?.Documents ?? [], actions);
        return new CandidateBanners(RunBanner(context), TooLargeBanner(context));
    }

    private static BannerViewModel? RunBanner(Context context)
    {
        var rule = Rules.FirstOrDefault(candidate => candidate.Matches(context));
        if (rule is null)
        {
            return null;
        }

        var content = rule.Build(context);
        return new BannerViewModel(rule.AppendsPartial && Completed(context) > 0 ? WithPartialSuffix(content) : content);
    }

    private static BannerContent WithPartialSuffix(BannerContent content) =>
        content with { Message = $"{content.Message} {CandidateStrings.PartialSuffix}" };

    private static int Completed(Context context) => context.Count(CandidateOutcome.Completed);

    private static bool IsAllKeysFailed(Context context)
    {
        if (context.State.ConfiguredKeyCount <= 1)
        {
            return false;
        }

        var failures = context.Documents.Where(document => document.Outcome is not (CandidateOutcome.Completed or CandidateOutcome.TooLarge)).ToList();
        return failures.Count > 0 && failures.All(document => document.Outcome is CandidateOutcome.KeyRejected or CandidateOutcome.Quota);
    }

    private static BannerContent Content(Context context, BannerSeverity severity, string title, string message) => new()
    {
        Severity = severity,
        Title = title,
        Message = message,
        DismissCommand = context.Actions.DismissRun,
    };

    private static BannerContent KeyMissing(Context context) => Content(
        context,
        BannerSeverity.Warning,
        CandidateStrings.KeyMissingTitle,
        CandidateStrings.KeyMissingMessage) with
    {
        ActionText = ExtractionStrings.OpenSettings,
        ActionName = CandidateStrings.OpenSettingsName,
        ActionCommand = context.Actions.OpenSettings,
    };

    private static BannerContent AllKeysFailed(Context context) => Content(
        context,
        BannerSeverity.Error,
        CandidateStrings.AllKeysFailedTitle,
        CandidateStrings.AllKeysFailedMessage(context.State.ConfiguredKeyCount));

    private static BannerContent KeyRejected(Context context) => Content(
        context,
        BannerSeverity.Error,
        CandidateStrings.KeyRejectedTitle,
        CandidateStrings.KeyRejectedMessage);

    private static BannerContent Quota(Context context) => Content(
        context,
        BannerSeverity.Warning,
        CandidateStrings.QuotaTitle,
        CandidateStrings.QuotaMessage);

    private static BannerContent Network(Context context) => Content(
        context,
        BannerSeverity.Error,
        CandidateStrings.NetworkTitle,
        CandidateStrings.NetworkMessage);

    private static BannerContent Failed(Context context)
    {
        var count = context.State.Failed ? context.State.Total : context.Count(CandidateOutcome.Failed);
        return Content(context, BannerSeverity.Error, CandidateStrings.FailedTitle, CandidateStrings.FailedMessage(count));
    }

    private static BannerContent Canceled(Context context) => Content(
        context,
        BannerSeverity.Info,
        CandidateStrings.CanceledTitle,
        CandidateStrings.CanceledMessage(Completed(context)));

    private static BannerViewModel? TooLargeBanner(Context context)
    {
        var large = context.Documents.Where(document => document.Outcome == CandidateOutcome.TooLarge).ToList();
        if (large.Count == 0)
        {
            return null;
        }

        return new BannerViewModel(new BannerContent
        {
            Severity = BannerSeverity.Warning,
            Title = CandidateStrings.TooLargeTitle(large.Count),
            Message = TooLargeLines(large),
            DismissCommand = context.Actions.DismissTooLarge,
        });
    }

    private static string TooLargeLines(IReadOnlyList<CandidateDocumentResult> large)
    {
        var lines = large.Take(MaxTooLargeLines).Select(TooLargeLine).ToList();
        if (large.Count > MaxTooLargeLines)
        {
            lines.Add(CandidateStrings.TooLargeMore(large.Count - MaxTooLargeLines));
        }

        return string.Join('\n', lines);
    }

    private static string TooLargeLine(CandidateDocumentResult document)
    {
        if (document is { TokenCount: { } tokens, TokenLimit: { } limit })
        {
            return CandidateStrings.TooLargeTokens(document.Filename, tokens, limit);
        }

        if (document is { SizeBytes: { } size, SizeCapBytes: { } cap })
        {
            return CandidateStrings.TooLargeSize(document.Filename, Megabytes(size), Megabytes(cap));
        }

        return document.Filename;
    }

    private static string Megabytes(long bytes) =>
        (bytes / BytesPerMegabyte).ToString("0.#", CultureInfo.CurrentCulture);
}
