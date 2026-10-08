using System.ComponentModel;
using System.Diagnostics;
using Collector.Application.Remote;

namespace Collector.Presentation.Services;

public interface IRemoteFetcher
{
    Task<RemoteFetchResult> FetchAsync(
        RemoteFetchRequest request,
        IProgress<RemoteFetchProgress> progress,
        CancellationToken cancellationToken);
}

public sealed class RemoteFetcher(RemoteFetchService service) : IRemoteFetcher
{
    public Task<RemoteFetchResult> FetchAsync(
        RemoteFetchRequest request,
        IProgress<RemoteFetchProgress> progress,
        CancellationToken cancellationToken) =>
        service.FetchAsync(request, progress, cancellationToken);
}

public interface IExternalLinkLauncher
{
    bool TryOpen(Uri uri);
}

public sealed class ShellLinkLauncher : IExternalLinkLauncher
{
    public bool TryOpen(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
