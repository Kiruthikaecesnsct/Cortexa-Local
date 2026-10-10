using Collector.Application.Remote;
using Collector.Domain.Enums;

namespace Collector.Infrastructure.Remote;

internal sealed class GuardedReadStream(Stream inner, SourceType provider, TimeSpan? idleTimeout = null) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (idleTimeout is { } limit)
        {
            idle.CancelAfter(limit);
        }

        try
        {
            return await inner.ReadAsync(buffer, idle.Token);
        }
        catch (Exception exception) when (IsUpstreamFailure(exception, cancellationToken))
        {
            throw new RemoteSourceException(RemoteFailureKind.Upstream, provider, null, exception);
        }
    }

    private static bool IsUpstreamFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
