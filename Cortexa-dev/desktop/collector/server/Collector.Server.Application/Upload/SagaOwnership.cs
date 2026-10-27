using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Upload;

public static class SagaOwnership
{
    public static bool OwnedBy(SagaRow saga, UploadCaller caller) =>
        string.Equals(saga.OwnerUserId, caller.UserId, StringComparison.Ordinal) &&
        string.Equals(saga.OrgId, caller.OrgId, StringComparison.Ordinal);
}
