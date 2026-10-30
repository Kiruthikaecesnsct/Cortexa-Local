namespace Collector.Infrastructure.Ai;

internal static class VertexEndpoints
{
    public static string BaseAddress(string location) => $"https://{location}-aiplatform.googleapis.com/";

    public static string GenerateContentPath(string projectId, string location, string model) =>
        $"v1/projects/{projectId}/locations/{location}/publishers/google/models/{model}:generateContent";
}
