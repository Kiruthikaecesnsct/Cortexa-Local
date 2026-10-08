using System.Net.Http.Headers;
using System.Text;

namespace Collector.Infrastructure.Remote;

public enum PatScheme
{
    Bearer,
    Basic,
}

public static class PatSchemes
{
    public static AuthenticationHeaderValue CreateHeader(PatScheme scheme, string pat) => scheme switch
    {
        PatScheme.Basic => new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat}"))),
        _ => new AuthenticationHeaderValue("Bearer", pat),
    };
}
