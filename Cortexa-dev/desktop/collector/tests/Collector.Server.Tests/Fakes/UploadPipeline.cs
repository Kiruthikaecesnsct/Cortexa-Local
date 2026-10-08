using Collector.Domain.Upload;
using Collector.Server.Application.Handlers;
using Collector.Server.Application.Upload;
using Collector.Server.Application.Upload.Validation;
using Collector.Server.Tests.Upload;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Collector.Server.Tests.Fakes;

internal sealed class UploadPipeline
{
    public UploadPipeline(Action<UploadOptions>? configure = null)
    {
        var options = Options.Create(UploadTestOptions.Create(configure));
        Preparer = new UploadPreparer(new KnowledgeUploadValidator(options), Models, options, new FixedTimeProvider());
        var dispatcher = new IngestionEventDispatcher(
            Publisher,
            new FixedClock(),
            NullLogger<IngestionEventDispatcher>.Instance);
        var writer = new WriteKnowledgeBatchHandler(
            Store,
            dispatcher,
            new BatchRollback(Store, NullLogger<BatchRollback>.Instance),
            new FixedClock(),
            NullLogger<WriteKnowledgeBatchHandler>.Instance);
        var replayer = new UploadReplayHandler(Store, dispatcher, NullLogger<UploadReplayHandler>.Instance);
        Handler = new SubmitKnowledgeUploadHandler(Store, Preparer, writer, replayer);
    }

    public FakePipelineRowStore Store { get; } = new();

    public FakeIngestionEventPublisher Publisher { get; } = new();

    public FakeModelConfigReader Models { get; } = new();

    public UploadPreparer Preparer { get; }

    public SubmitKnowledgeUploadHandler Handler { get; }

    public Task<UploadOutcome> SubmitAsync(KnowledgeUploadRequest request, UploadCaller? caller = null, string? key = null) =>
        Handler.HandleAsync(
            new SubmitKnowledgeUploadCommand(request, caller ?? TestIdentity.Caller, key ?? TestIdentity.IdempotencyKey),
            TestContext.Current.CancellationToken);
}
