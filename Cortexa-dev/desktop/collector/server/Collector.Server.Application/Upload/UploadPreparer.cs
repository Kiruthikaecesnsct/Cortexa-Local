using Collector.Domain.Upload;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Upload.Validation;
using Microsoft.Extensions.Options;

namespace Collector.Server.Application.Upload;

public sealed class UploadPreparer(
    KnowledgeUploadValidator validator,
    IModelConfigReader modelConfigReader,
    IOptions<UploadOptions> options,
    TimeProvider timeProvider)
{
    public async Task<UploadPreparation> PrepareAsync(
        KnowledgeUploadRequest request,
        UploadCaller caller,
        string batchId,
        CancellationToken cancellationToken)
    {
        var validation = validator.Validate(request);
        if (validation.Request is null)
        {
            return UploadPreparation.Invalid(validation.Errors);
        }

        var models = await modelConfigReader.ReadAsync(cancellationToken);
        var context = new UploadMappingContext(
            caller,
            batchId,
            models,
            options.Value.Engine.ToLowerInvariant(),
            timeProvider.GetUtcNow());
        return UploadPreparation.Ready(UploadCommandMapper.Map(validation.Request, context));
    }
}

public sealed record UploadPreparation(
    WriteKnowledgeBatchCommand? Command,
    IReadOnlyList<UploadValidationError> Errors)
{
    public static UploadPreparation Ready(WriteKnowledgeBatchCommand command) => new(command, []);

    public static UploadPreparation Invalid(IReadOnlyList<UploadValidationError> errors) => new(null, errors);
}
