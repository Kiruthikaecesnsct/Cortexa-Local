using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Presentation;

public sealed class BedrockSsoRowViewModelTests
{
    private readonly FakeBedrockSsoCredentials _credentials = new();

    private BedrockSsoRowViewModel Row() => new(_credentials, NullLogger.Instance);

    [Fact]
    public async Task LoadStatusAsync_NotConnected_ShowsNotConnectedChip()
    {
        var row = Row();

        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BedrockSsoChipKind.NotConnected, row.ChipKind);
        Assert.True(row.ShowConnect);
        Assert.False(row.ShowDisconnect);
    }

    [Fact]
    public async Task LoadStatusAsync_Connected_ShowsConnectedChip()
    {
        _credentials.Status = new BedrockSsoStatus(true, DateTimeOffset.UtcNow.AddHours(1));
        var row = Row();

        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BedrockSsoChipKind.Connected, row.ChipKind);
        Assert.True(row.ShowDisconnect);
        Assert.False(row.ShowConnect);
    }

    [Fact]
    public async Task LoadStatusAsync_StatusCheckThrows_ShowsUnknownChipWithoutCrashing()
    {
        _credentials.StatusFailure = new InvalidOperationException("sso unreachable");
        var row = Row();

        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BedrockSsoChipKind.Unknown, row.ChipKind);
        Assert.True(row.ShowStatusHelper);
    }

    [Fact]
    public async Task LoadStatusAsync_AlreadyResolved_DoesNotQueryAgain()
    {
        var row = Row();
        await row.LoadStatusAsync(TestContext.Current.CancellationToken);
        _credentials.StatusCalls = 0;

        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, _credentials.StatusCalls);
    }

    [Fact]
    public async Task ConnectCommand_Success_TransitionsToConnectedWithMessage()
    {
        var row = Row();

        await row.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(BedrockSsoChipKind.Connected, row.ChipKind);
        Assert.True(row.HasMessage);
        Assert.False(row.HasError);
        Assert.False(row.IsConnecting);
    }

    [Fact]
    public async Task ConnectCommand_Failure_SurfacesErrorWithoutCrashing()
    {
        _credentials.ConnectFailure = new InvalidOperationException("device flow timed out");
        var row = Row();

        await row.ConnectCommand.ExecuteAsync(null);

        Assert.True(row.HasError);
        Assert.False(row.HasMessage);
        Assert.NotEqual(BedrockSsoChipKind.Connected, row.ChipKind);
        Assert.False(row.IsConnecting);
    }

    [Fact]
    public async Task DisconnectCommand_Success_ClearsStatusBackToNotConnected()
    {
        _credentials.Status = new BedrockSsoStatus(true, DateTimeOffset.UtcNow.AddHours(1));
        var row = Row();
        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        await row.DisconnectCommand.ExecuteAsync(null);

        Assert.Equal(BedrockSsoChipKind.NotConnected, row.ChipKind);
        Assert.True(row.HasMessage);
        Assert.False(row.IsDisconnecting);
    }

    [Fact]
    public async Task DisconnectCommand_Failure_SurfacesErrorWithoutCrashing()
    {
        _credentials.DisconnectFailure = new InvalidOperationException("could not clear token");
        _credentials.Status = new BedrockSsoStatus(true, DateTimeOffset.UtcNow.AddHours(1));
        var row = Row();
        await row.LoadStatusAsync(TestContext.Current.CancellationToken);

        await row.DisconnectCommand.ExecuteAsync(null);

        Assert.True(row.HasError);
        Assert.False(row.IsDisconnecting);
    }

    private sealed class FakeBedrockSsoCredentials : IBedrockSsoCredentials
    {
        public BedrockSsoStatus Status { get; set; } = BedrockSsoStatus.NotConnected;

        public Exception? StatusFailure { get; set; }

        public Exception? ConnectFailure { get; set; }

        public Exception? DisconnectFailure { get; set; }

        public int StatusCalls { get; set; }

        public Task<BedrockSsoStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            StatusCalls++;
            return StatusFailure is null ? Task.FromResult(Status) : Task.FromException<BedrockSsoStatus>(StatusFailure);
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (ConnectFailure is not null)
            {
                return Task.FromException(ConnectFailure);
            }

            Status = new BedrockSsoStatus(true, DateTimeOffset.UtcNow.AddHours(1));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (DisconnectFailure is not null)
            {
                return Task.FromException(DisconnectFailure);
            }

            Status = BedrockSsoStatus.NotConnected;
            return Task.CompletedTask;
        }
    }
}
