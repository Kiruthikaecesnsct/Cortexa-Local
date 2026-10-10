using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Remote;
using Collector.Tests.Support;

namespace Collector.Tests.Remote;

public sealed class GuardedReadStreamTests
{
    private const int IdleMilliseconds = 50;
    private const int BufferSize = 8;

    [Fact]
    public async Task ReadAsync_StalledStream_ThrowsUpstreamAfterIdleTimeout()
    {
        await using var stalled = new StalledStream();
        await using var guarded = new GuardedReadStream(stalled, SourceType.Github, TimeSpan.FromMilliseconds(IdleMilliseconds));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            async () => await guarded.ReadExactlyAsync(new byte[BufferSize], TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
    }

    [Fact]
    public async Task ReadAsync_CallerCancels_PropagatesCancellation()
    {
        await using var stalled = new StalledStream();
        await using var guarded = new GuardedReadStream(stalled, SourceType.Github, TimeSpan.FromMinutes(1));
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await guarded.ReadExactlyAsync(new byte[BufferSize], caller.Token));
    }

    private sealed class StalledStream : Stream
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
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
