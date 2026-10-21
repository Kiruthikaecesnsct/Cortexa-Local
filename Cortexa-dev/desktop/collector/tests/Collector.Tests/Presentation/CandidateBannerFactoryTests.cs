using System.Globalization;
using Collector.Application.Candidates;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Tests.Presentation;

public sealed class CandidateBannerFactoryTests
{
    private const int SeveralKeys = 3;
    private const int OneKey = 1;
    private const int MaxLines = 5;
    private const int OverflowCount = 3;
    private const long OneMegabyte = 1024 * 1024;
    private const int TokenCount = 2000;
    private const int TokenLimit = 1000;

    private static readonly CandidateBannerActions Actions = new(
        new RelayCommand(() => { }),
        new RelayCommand(() => { }),
        new RelayCommand(() => { }));

    private static CandidateBanners Create(CandidateRunOutcome outcome) =>
        CandidateBannerFactory.Create(CandidateData.StateFor(outcome), Actions);

    private static CandidateBanners Create(int keys, params CandidateDocumentResult[] documents) =>
        Create(CandidateData.Outcome(CandidateData.Result(documents), keys: keys));

    private static CandidateDocumentResult Doc(CandidateOutcome outcome, string name = "a.txt") => CandidateData.Doc(outcome, name);

    [Fact]
    public void Create_AllCompleted_ReturnsNoBanners()
    {
        var banners = Create(0, CandidateData.Completed("a.txt"), CandidateData.Completed("b.txt"));

        Assert.Null(banners.Run);
        Assert.Null(banners.TooLarge);
    }

    [Fact]
    public void Create_KeyMissing_ReturnsWarningWithOpenSettingsAction()
    {
        var banner = Create(0, Doc(CandidateOutcome.KeyMissing)).Run!;

        Assert.Equal(BannerSeverity.Warning, banner.Severity);
        Assert.Equal(CandidateStrings.KeyMissingTitle, banner.Title);
        Assert.Equal(CandidateStrings.KeyMissingMessage, banner.Message);
        Assert.Same(Actions.OpenSettings, banner.ActionCommand);
        Assert.Same(Actions.DismissRun, banner.DismissCommand);
    }

    [Fact]
    public void Create_KeyRejectedWithOneKey_ReturnsSingleKeyErrorBanner()
    {
        var banner = Create(OneKey, Doc(CandidateOutcome.KeyRejected)).Run!;

        Assert.Equal(BannerSeverity.Error, banner.Severity);
        Assert.Equal(CandidateStrings.KeyRejectedTitle, banner.Title);
        Assert.Equal(CandidateStrings.KeyRejectedMessage, banner.Message);
    }

    [Fact]
    public void Create_QuotaWithOneKey_ReturnsQuotaWarning()
    {
        var banner = Create(OneKey, Doc(CandidateOutcome.Quota)).Run!;

        Assert.Equal(BannerSeverity.Warning, banner.Severity);
        Assert.Equal(CandidateStrings.QuotaTitle, banner.Title);
        Assert.Equal(CandidateStrings.QuotaMessage, banner.Message);
    }

    [Fact]
    public void Create_Network_ReturnsNetworkError()
    {
        var banner = Create(0, Doc(CandidateOutcome.Network)).Run!;

        Assert.Equal(BannerSeverity.Error, banner.Severity);
        Assert.Equal(CandidateStrings.NetworkTitle, banner.Title);
        Assert.Equal(CandidateStrings.NetworkMessage, banner.Message);
    }

    [Fact]
    public void Create_FailedDocuments_CountsThemInMessage()
    {
        const int FailedCount = 2;

        var banner = Create(0, Doc(CandidateOutcome.Failed, "a.txt"), Doc(CandidateOutcome.Failed, "b.txt")).Run!;

        Assert.Equal(BannerSeverity.Error, banner.Severity);
        Assert.Equal(CandidateStrings.FailedTitle, banner.Title);
        Assert.Equal(CandidateStrings.FailedMessage(FailedCount), banner.Message);
    }

    [Fact]
    public void Create_RunnerFailed_UsesTotalDocumentCountInMessage()
    {
        const int Total = 4;
        var state = new CandidateRunState();
        state.Begin(Total);
        state.Set(CandidateRunOutcome.Failed());

        var banner = CandidateBannerFactory.Create(state, Actions).Run!;

        Assert.Equal(CandidateStrings.FailedTitle, banner.Title);
        Assert.Equal(CandidateStrings.FailedMessage(Total), banner.Message);
    }

    [Fact]
    public void Create_CanceledFlagWithFinishedDocuments_ReturnsInfoBannerWithoutPartialSuffix()
    {
        var outcome = CandidateData.Outcome(
            CandidateData.Result(CandidateData.Completed("a.txt"), Doc(CandidateOutcome.Canceled, "b.txt")),
            canceled: true);

        var banner = Create(outcome).Run!;

        Assert.Equal(BannerSeverity.Info, banner.Severity);
        Assert.Equal(CandidateStrings.CanceledTitle, banner.Title);
        Assert.Equal(CandidateStrings.CanceledMessage(1), banner.Message);
        Assert.DoesNotContain(CandidateStrings.PartialSuffix, banner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AllKeysFailedWithSeveralKeys_ReturnsAllKeysFailedBanner()
    {
        var banner = Create(SeveralKeys, Doc(CandidateOutcome.KeyRejected, "a.txt"), Doc(CandidateOutcome.Quota, "b.txt")).Run!;

        Assert.Equal(BannerSeverity.Error, banner.Severity);
        Assert.Equal(CandidateStrings.AllKeysFailedTitle, banner.Title);
        Assert.Equal(CandidateStrings.AllKeysFailedMessage(SeveralKeys), banner.Message);
    }

    [Fact]
    public void Create_AllKeysFailedWithOneKey_FallsBackToKeyRejected()
    {
        var banner = Create(OneKey, Doc(CandidateOutcome.KeyRejected), Doc(CandidateOutcome.Quota, "b.txt")).Run!;

        Assert.Equal(CandidateStrings.KeyRejectedTitle, banner.Title);
    }

    [Fact]
    public void Create_KeyFailuresMixedWithOtherFailureAndSeveralKeys_FallsBackToKeyRejected()
    {
        var banner = Create(SeveralKeys, Doc(CandidateOutcome.KeyRejected), Doc(CandidateOutcome.Network, "b.txt")).Run!;

        Assert.Equal(CandidateStrings.KeyRejectedTitle, banner.Title);
    }

    [Fact]
    public void Create_AllKeysFailedIgnoresCompletedAndTooLargeDocuments()
    {
        var banner = Create(
            SeveralKeys,
            Doc(CandidateOutcome.Quota),
            CandidateData.Completed("b.txt"),
            CandidateData.TooLargeTokens("c.txt")).Run!;

        Assert.Equal(CandidateStrings.AllKeysFailedTitle, banner.Title);
    }

    [Theory]
    [MemberData(nameof(PrecedenceCases))]
    public void Create_MixedOutcomes_HighestPrecedenceWins(CandidateOutcome[] outcomes, string expectedTitle)
    {
        var documents = outcomes.Select((outcome, index) => Doc(outcome, $"f{index}.txt")).ToArray();

        var banner = Create(OneKey, documents).Run!;

        Assert.Equal(expectedTitle, banner.Title);
    }

    public static TheoryData<CandidateOutcome[], string> PrecedenceCases() => new()
    {
        { [CandidateOutcome.Failed, CandidateOutcome.Network, CandidateOutcome.Quota, CandidateOutcome.KeyRejected, CandidateOutcome.KeyMissing], CandidateStrings.KeyMissingTitle },
        { [CandidateOutcome.Failed, CandidateOutcome.Network, CandidateOutcome.Quota, CandidateOutcome.KeyRejected], CandidateStrings.KeyRejectedTitle },
        { [CandidateOutcome.Failed, CandidateOutcome.Network, CandidateOutcome.Quota], CandidateStrings.QuotaTitle },
        { [CandidateOutcome.Failed, CandidateOutcome.Network], CandidateStrings.NetworkTitle },
        { [CandidateOutcome.Canceled, CandidateOutcome.Failed], CandidateStrings.FailedTitle },
        { [CandidateOutcome.Canceled], CandidateStrings.CanceledTitle },
    };

    [Fact]
    public void Create_AllKeysFailedAndKeyMissing_KeyMissingWins()
    {
        var banner = Create(SeveralKeys, Doc(CandidateOutcome.KeyRejected), Doc(CandidateOutcome.KeyMissing, "b.txt")).Run!;

        Assert.Equal(CandidateStrings.KeyMissingTitle, banner.Title);
    }

    [Theory]
    [InlineData(CandidateOutcome.KeyMissing)]
    [InlineData(CandidateOutcome.KeyRejected)]
    [InlineData(CandidateOutcome.Quota)]
    [InlineData(CandidateOutcome.Network)]
    [InlineData(CandidateOutcome.Failed)]
    public void Create_FailureWithCompletedDocument_AppendsPartialSuffix(CandidateOutcome failure)
    {
        var banner = Create(OneKey, CandidateData.Completed("a.txt"), Doc(failure, "b.txt")).Run!;

        Assert.EndsWith(CandidateStrings.PartialSuffix, banner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_FailureWithoutCompletedDocument_OmitsPartialSuffix()
    {
        var banner = Create(OneKey, Doc(CandidateOutcome.Network)).Run!;

        Assert.DoesNotContain(CandidateStrings.PartialSuffix, banner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_TooLargeByTokens_ListsFileWithCounts()
    {
        var banner = Create(0, CandidateData.TooLargeTokens("big.txt", TokenCount, TokenLimit)).TooLarge!;

        Assert.Equal(BannerSeverity.Warning, banner.Severity);
        Assert.Equal(CandidateStrings.TooLargeTitle(1), banner.Title);
        Assert.Equal(CandidateStrings.TooLargeTokens("big.txt", TokenCount, TokenLimit), banner.Message);
        Assert.Same(Actions.DismissTooLarge, banner.DismissCommand);
    }

    [Fact]
    public void Create_TooLargeBySize_ListsFileWithMegabytes()
    {
        const int SizeMegabytes = 3;
        const int CapMegabytes = 2;

        var banner = Create(0, CandidateData.TooLargeSize("big.pdf", SizeMegabytes * OneMegabyte, CapMegabytes * OneMegabyte)).TooLarge!;

        var expected = CandidateStrings.TooLargeSize(
            "big.pdf",
            SizeMegabytes.ToString(CultureInfo.CurrentCulture),
            CapMegabytes.ToString(CultureInfo.CurrentCulture));
        Assert.Equal(expected, banner.Message);
    }

    [Fact]
    public void Create_TooLargeWithoutDetails_ListsOnlyFilename()
    {
        var banner = Create(0, Doc(CandidateOutcome.TooLarge, "plain.txt")).TooLarge!;

        Assert.Equal("plain.txt", banner.Message);
    }

    [Fact]
    public void Create_MoreThanFiveTooLarge_CapsLinesAndAddsAndMore()
    {
        var documents = Enumerable.Range(0, MaxLines + OverflowCount)
            .Select(index => CandidateData.TooLargeTokens($"f{index}.txt"))
            .ToArray();

        var banner = Create(0, documents).TooLarge!;

        var lines = banner.Message!.Split('\n');
        Assert.Equal(MaxLines + 1, lines.Length);
        Assert.Equal(CandidateStrings.TooLargeMore(OverflowCount), lines[^1]);
        Assert.Equal(CandidateStrings.TooLargeTitle(MaxLines + OverflowCount), banner.Title);
    }

    [Fact]
    public void Create_ExactlyFiveTooLarge_HasNoAndMoreLine()
    {
        var documents = Enumerable.Range(0, MaxLines).Select(index => CandidateData.TooLargeTokens($"f{index}.txt")).ToArray();

        var banner = Create(0, documents).TooLarge!;

        Assert.Equal(MaxLines, banner.Message!.Split('\n').Length);
    }

    [Fact]
    public void Create_TooLargeAndKeyFailure_ReturnsBothBanners()
    {
        var banners = Create(OneKey, CandidateData.TooLargeTokens("big.txt"), Doc(CandidateOutcome.Quota, "b.txt"));

        Assert.NotNull(banners.TooLarge);
        Assert.Equal(CandidateStrings.QuotaTitle, banners.Run!.Title);
    }
}
