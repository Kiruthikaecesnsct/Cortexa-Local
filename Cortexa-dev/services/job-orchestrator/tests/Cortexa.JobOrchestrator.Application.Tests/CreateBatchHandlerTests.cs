using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class CreateBatchHandlerTests
{
    private readonly IBlobStorageWriter _blob;
    private readonly IDocumentRepository _documents;
    private readonly ISagaRepository _sagas;
    private readonly IGitPatSecretStore _gitPatStore;
    private readonly Contracts.IConfigRepository _configRepository;
    private readonly CreateBatchHandler _handler;

    public CreateBatchHandlerTests()
    {
        _blob = Substitute.For<IBlobStorageWriter>();
        _documents = Substitute.For<IDocumentRepository>();
        _sagas = Substitute.For<ISagaRepository>();
        _gitPatStore = Substitute.For<IGitPatSecretStore>();
        _configRepository = Substitute.For<Contracts.IConfigRepository>();
        _configRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(
            new ModelConfig("gpt-5.5", "gpt-5.4", "gpt-5.5", "gpt-5.5", SeedingModes.Deep, DateTimeOffset.UtcNow));
        _handler = new CreateBatchHandler(_blob, _documents, _sagas, _gitPatStore, _configRepository);
    }

    private static CreateBatchCommand BuildCommand(int fileCount = 2) =>
        new(
            Files: Enumerable.Range(0, fileCount)
                .Select(i => ($"file{i}.pdf", (Stream)new MemoryStream([1, 2, 3]), "application/pdf"))
                .ToList(),
            BatchName: "Test Batch",
            Engine: "harvesting",
            AiModel: "gpt",
            SeedCorpusDomain: "ml_ai");

    [Fact]
    public async Task HandleAsync_ValidCommand_ReturnsResponseWithCorrectDocumentCount()
    {
        _blob.SaveRawAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.com/raw/doc");

        var result = await _handler.HandleAsync(BuildCommand(3), CancellationToken.None);

        result.DocumentCount.Should().Be(3);
        result.Status.Should().Be("pending");
        result.BatchId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_UploadsBlobForEachFile()
    {
        _blob.SaveRawAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.com/raw/doc");

        await _handler.HandleAsync(BuildCommand(2), CancellationToken.None);

        await _blob.Received(2).SaveRawAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesDocumentRecordsInRepository()
    {
        _blob.SaveRawAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.com/raw/doc");

        IReadOnlyList<DocumentRecord>? capturedRecords = null;
        await _documents.CreateManyAsync(
            Arg.Do<IReadOnlyList<DocumentRecord>>(r => capturedRecords = r),
            Arg.Any<CancellationToken>());

        await _handler.HandleAsync(BuildCommand(2), CancellationToken.None);

        capturedRecords.Should().NotBeNull().And.HaveCount(2);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesSagaWithMetadata()
    {
        _blob.SaveRawAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.com/raw/doc");

        BatchSaga? capturedSaga = null;
        await _sagas.CreateAsync(Arg.Do<BatchSaga>(s => capturedSaga = s), Arg.Any<CancellationToken>());

        var command = BuildCommand(1);
        await _handler.HandleAsync(command, CancellationToken.None);

        capturedSaga.Should().NotBeNull();
        capturedSaga!.Metadata.Should().NotBeNull();
        capturedSaga.Metadata!.BatchName.Should().Be("Test Batch");
        capturedSaga.Metadata.Engine.Should().Be("harvesting");
        capturedSaga.Metadata.TotalDocumentCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_SnapshotsSeedingModeFromConfigIntoMetadata()
    {
        _blob.SaveRawAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.com/raw/doc");

        BatchSaga? capturedSaga = null;
        await _sagas.CreateAsync(Arg.Do<BatchSaga>(s => capturedSaga = s), Arg.Any<CancellationToken>());

        await _handler.HandleAsync(BuildCommand(1), CancellationToken.None);

        capturedSaga!.Metadata!.SeedingMode.Should().Be(SeedingModes.Deep);
    }

    [Fact]
    public async Task HandleAsync_HarvestingEngine_SetsBatchWantsHarvestingTrue()
    {
        _blob.SaveRawAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.com/raw/doc");

        BatchSaga? capturedSaga = null;
        await _sagas.CreateAsync(Arg.Do<BatchSaga>(s => capturedSaga = s), Arg.Any<CancellationToken>());

        await _handler.HandleAsync(BuildCommand(), CancellationToken.None);

        capturedSaga!.WantsHarvesting.Should().BeTrue();
        capturedSaga.WantsSeeding.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_SeedingEngine_SetsBatchWantsSeedingTrue()
    {
        _blob.SaveRawAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.com/raw/doc");

        BatchSaga? capturedSaga = null;
        await _sagas.CreateAsync(Arg.Do<BatchSaga>(s => capturedSaga = s), Arg.Any<CancellationToken>());

        var command = new CreateBatchCommand(
            Files: [(("file.pdf", new MemoryStream([1]), "application/pdf"))],
            BatchName: "B", Engine: "seeding", AiModel: "gpt", SeedCorpusDomain: "ml_ai");

        await _handler.HandleAsync(command, CancellationToken.None);

        capturedSaga!.WantsHarvesting.Should().BeFalse();
        capturedSaga.WantsSeeding.Should().BeTrue();
    }
}
