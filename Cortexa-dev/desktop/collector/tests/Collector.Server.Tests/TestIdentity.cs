using Collector.Server.Application.Upload;

namespace Collector.Server.Tests;

internal static class TestIdentity
{
    public const string UserId = "3f2b8c1e-5d4a-4e7b-9a10-6c2d8e4f1a77";
    public const string OtherUserId = "9a1c7d20-44be-4c0f-8e53-b1d2f6a09c35";
    public const string OrgId = "org-1";
    public const string OtherOrgId = "org-2";
    public const string IdempotencyKey = "key-1";

    public static readonly Guid Stamp = new("c7a4f1d2-9b3e-4a68-8d05-2e1f7b6c9a43");

    public static UploadCaller Caller => new(UserId, OrgId);
}
