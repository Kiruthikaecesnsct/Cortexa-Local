namespace Collector.Infrastructure.Ai;

internal static class VertexEndpoints
{
    private const string GlobalLocation = "global";

    public static string BaseAddress(string location) =>
        string.Equals(location, GlobalLocation, StringComparison.OrdinalIgnoreCase)
            ? "https://aiplatform.googleapis.com/"
            : $"https://{location}-aiplatform.googleapis.com/";

    public static string GenerateContentPath(string projectId, string location, string model) =>
        $"v1/projects/{projectId}/locations/{location}/publishers/google/models/{model}:generateContent";

    public static Uri GenerateContentUri(string projectId, string location, string model) =>
        new(new Uri(BaseAddress(location)), GenerateContentPath(projectId, location, model));
}
