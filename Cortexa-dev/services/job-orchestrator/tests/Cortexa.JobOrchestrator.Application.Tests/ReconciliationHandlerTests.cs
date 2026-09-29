using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Application.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class ReconciliationHandlerTests
{
    private readonly IBatchExistenceQuery _existenceQuery = Substitute.For<IBatchExistenceQuery>();
    private readonly IOrphanScanner _scanner = Substitute.For<IOrphanScanner>();
    private readonly IServiceBusStuckScanner _sbScanner = Substitute.For<IServiceBusStuckScanner>();
    private readonly ISagaReferenceScanner _refScanner = Substitute.For<ISagaReferenceScanner>();
    private readonly ISagaRepository _sagas = Substitute.For<ISagaRepository>();
    private readonly IBatchDeleter _deleter = Substitute.For<IBatchDeleter>();
    private readonly ILogger<DeleteBatchHandler> _deleteLogger = Substitute.For<ILogger<DeleteBatchHandler>>();
    private readonly ILogger<ReconciliationHandler> _reconLogger = Substitute.For<ILogger<ReconciliationHandler>>();

    public ReconciliationHandlerTests()
    {
        _scanner.StoreName.Returns("test-store");
        _sbScanner.ListStuckBatchIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        _refScanner.FindDanglingAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DanglingReference>());
        _deleter.StoreName.Returns("test-deleter");
    }

    private ReconciliationHandler BuildHandler(ReconciliationOptions options)
    {
        var deps = new ReconciliationScannerDependencies(
            new[] { _scanner },
            _existenceQuery,
            _sbScanner,
            _refScanner);

        return new ReconciliationHandler(
            deps,
            new DeleteBatchHandler(_sagas, new[] { _deleter }, _deleteLogger),
            Options.Create(options),
            _reconLogger);
    }

    private void SetupLiveBatchIds(params string[] ids) =>
        _existenceQuery.GetLiveBatchIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(ids));

    private void SetupScannerIds(params string[] ids) =>
        _scanner.ListBatchIdsAsync(Arg.Any<CancellationToken>())
            .Returns(ids.ToArray() as IReadOnlyCollection<string>);

    private void SetupDeleterSuccess()
    {
        _deleter
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(ci => new StoreDeletionResult("test-deleter", 1, true));
        _sagas.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.BatchSaga?)null);
    }

    [Fact]
    public async Task ScanAsync_ReturnsReadOnlyReport_WithNoDeletes()
    {
        SetupLiveBatchIds("live-1");
        SetupScannerIds("live-1", "orphan-1");

        var handler = BuildHandler(new ReconciliationOptions());

        var report = await handler.ScanAsync(CancellationToken.None);

        report.DryRun.Should().BeTrue();
        report.ReconciledCount.Should().Be(0);
        await _deleter.DidNotReceive()
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAsync_OrphanDiff_BatchIdInScannerButNotLive_AppearsInReport()
    {
        SetupLiveBatchIds("live-1");
        SetupScannerIds("live-1", "orphan-1");

        var handler = BuildHandler(new ReconciliationOptions());

        var report = await handler.ScanAsync(CancellationToken.None);

        report.TotalOrphanBatches.Should().Be(1);
        report.PerStore.Should().Contain(s => s.StoreName == "test-store" && s.BatchIds.Contains("orphan-1"));
    }

    [Fact]
    public async Task ScanAsync_NoFalsePositive_BatchIdInBothScannerAndLive_NotInReport()
    {
        SetupLiveBatchIds("live-1", "live-2");
        SetupScannerIds("live-1", "live-2");

        var handler = BuildHandler(new ReconciliationOptions());

        var report = await handler.ScanAsync(CancellationToken.None);

        report.TotalOrphanBatches.Should().Be(0);
        report.PerStore.Should().AllSatisfy(s => s.OrphanCount.Should().Be(0));
    }

    [Fact]
    public async Task ScanAsync_SeverityBlocker_WhenOrphanCountMeetsThreshold()
    {
        SetupLiveBatchIds();
        var orphans = Enumerable.Range(1, 100).Select(i => $"orphan-{i}").ToArray();
        SetupScannerIds(orphans);

        var handler = BuildHandler(new ReconciliationOptions { BlockerOrphanThreshold = 100 });

        var report = await handler.ScanAsync(CancellationToken.None);

        report.Severity.Should().Be(ReconciliationSeverity.Blocker);
    }

    [Fact]
    public async Task ScanAsync_SeverityWarning_WhenOrphanCountAboveZero()
    {
        SetupLiveBatchIds("live-1");
        SetupScannerIds("live-1", "orphan-1");

        var handler = BuildHandler(new ReconciliationOptions { BlockerOrphanThreshold = 100 });

        var report = await handler.ScanAsync(CancellationToken.None);

        report.Severity.Should().Be(ReconciliationSeverity.Warning);
    }

    [Fact]
    public async Task ScanAsync_SeverityInfo_WhenNoOrphans()
    {
        SetupLiveBatchIds("live-1");
        SetupScannerIds("live-1");

        var handler = BuildHandler(new ReconciliationOptions());

        var report = await handler.ScanAsync(CancellationToken.None);

        report.Severity.Should().Be(ReconciliationSeverity.Info);
    }

    [Fact]
    public async Task ReconcileAsync_ConfirmedFalse_ReturnsNoDeletes()
    {
        SetupLiveBatchIds();
        SetupScannerIds("orphan-1");

        var handler = BuildHandler(new ReconciliationOptions { ReconcileEnabled = true });

        var report = await handler.ReconcileAsync(confirmed: false, CancellationToken.None);

        report.DryRun.Should().BeTrue();
        report.ReconciledCount.Should().Be(0);
        await _deleter.DidNotReceive()
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_ConfirmedTrueButReconcileDisabled_ReturnsNoDeletes()
    {
        SetupLiveBatchIds();
        SetupScannerIds("orphan-1");

        var handler = BuildHandler(new ReconciliationOptions { ReconcileEnabled = false });

        var report = await handler.ReconcileAsync(confirmed: true, CancellationToken.None);

        report.DryRun.Should().BeTrue();
        report.ReconciledCount.Should().Be(0);
        await _deleter.DidNotReceive()
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_ConfirmedAndEnabled_DeletesOrphanedBatchIds_SkipsLive()
    {
        SetupLiveBatchIds("live-1");
        SetupScannerIds("live-1", "orphan-1");
        SetupDeleterSuccess();

        var handler = BuildHandler(new ReconciliationOptions { ReconcileEnabled = true });

        var report = await handler.ReconcileAsync(confirmed: true, CancellationToken.None);

        report.DryRun.Should().BeFalse();
        report.ReconciledCount.Should().Be(1);
        await _deleter.Received(1)
            .DeleteAsync("orphan-1", Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
        await _deleter.DidNotReceive()
            .DeleteAsync("live-1", Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_SecondRunFindsZeroOrphans_ProducesZeroDeletes()
    {
        SetupLiveBatchIds("live-1");
        SetupScannerIds("live-1");

        var handler = BuildHandler(new ReconciliationOptions { ReconcileEnabled = true });

        var report1 = await handler.ReconcileAsync(confirmed: true, CancellationToken.None);
        var report2 = await handler.ReconcileAsync(confirmed: true, CancellationToken.None);

        report1.TotalOrphanBatches.Should().Be(0);
        report2.TotalOrphanBatches.Should().Be(0);
        report1.Errors.Should().BeEmpty();
        report2.Errors.Should().BeEmpty();
        await _deleter.DidNotReceive()
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
    }
}
