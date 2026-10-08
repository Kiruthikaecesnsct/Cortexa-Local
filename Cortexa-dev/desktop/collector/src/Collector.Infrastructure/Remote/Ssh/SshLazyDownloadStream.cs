namespace Collector.Infrastructure.Remote.Ssh;

internal sealed class SshLazyDownloadStream(
    Func<CancellationToken, Task<Stream>> materialize,
    CancellationToken fallbackCancellationToken) : Stream
{
    private Stream? _inner;

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
        var inner = _inner ??= await materialize(cancellationToken.CanBeCanceled ? cancellationToken : fallbackCancellationToken);
        return await inner.ReadAsync(buffer, cancellationToken);
    }

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
            _inner?.Dispose();
        }

        base.Dispose(disposing);
    }
}
