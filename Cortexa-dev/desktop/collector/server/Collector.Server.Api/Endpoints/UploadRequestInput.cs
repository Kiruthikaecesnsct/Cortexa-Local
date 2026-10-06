using Collector.Server.Application.Upload;

namespace Collector.Server.Api.Endpoints;

public sealed record UploadRequestInput(SubmitKnowledgeUploadCommand? Command, IResult? Failure)
{
    public static UploadRequestInput Accepted(SubmitKnowledgeUploadCommand command) => new(command, null);

    public static UploadRequestInput Rejected(IResult failure) => new(null, failure);
}
