using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Collector.Application.Remote;
using Collector.Application.Secrets;
using Collector.Domain.Enums;

namespace Collector.Infrastructure.Remote;

public interface IRemoteErrorMapper
{
    bool IsSuccess(HttpResponseMessage response);

    RemoteSourceException Map(HttpResponseMessage response);
}

public sealed class RemoteHttp(
    HttpClient client,
    IRemoteErrorMapper mapper,
    SourceType provider,
    TimeSpan? readIdleTimeout = null)
{
    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken cancellationToken) =>
        GuardAsync(() => client.SendAsync(request, completion, cancellationToken), cancellationToken);

    public async Task<HttpResponseMessage> SendCheckedAsync(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(request, completion, cancellationToken);
        try
        {
            EnsureSuccess(response);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public void EnsureSuccess(HttpResponseMessage response)
    {
        if (!mapper.IsSuccess(response))
        {
            throw mapper.Map(response);
        }
    }

    public Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken) =>
        GuardAsync(
            async () => await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
                ?? throw new JsonException("Empty response body."),
            cancellationToken);

    public async Task<RemoteBlob> OpenBlobAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await SendCheckedAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            var stream = await GuardAsync(() => response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken);
            return new RemoteBlob(new GuardedReadStream(stream, provider, readIdleTimeout), response.Content.Headers.ContentLength, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<T> GuardAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException or SecretStoreException)
        {
            throw Upstream(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Upstream(exception);
        }
    }

    private RemoteSourceException Upstream(Exception inner) =>
        new(RemoteFailureKind.Upstream, provider, null, inner);
}
