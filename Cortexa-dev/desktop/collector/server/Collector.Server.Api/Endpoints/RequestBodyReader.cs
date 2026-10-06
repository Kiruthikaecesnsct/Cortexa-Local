using Microsoft.AspNetCore.Http.Features;

namespace Collector.Server.Api.Endpoints;

public static class RequestBodyReader
{
    private const int BufferSize = 81920;

    public static async Task<byte[]?> ReadAsync(HttpRequest request, long maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes)
        {
            return null;
        }

        LimitServerBody(request.HttpContext, maxBytes);
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void LimitServerBody(HttpContext http, long maxBytes)
    {
        var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = maxBytes;
        }
    }
}
