using System.Text.Json;
using Collector.Application.Knowledge;

namespace Collector.Application.Candidates;

public static class CandidatePromptLoader
{
    private const string PromptResource = "candidate-prompt.v1.json";
    private const string SchemaResource = "candidate-output.schema.json";

    public static CandidatePrompt Load()
    {
        using var prompt = JsonDocument.Parse(EmbeddedPromptResource.Read(PromptResource));
        return new CandidatePrompt
        {
            Version = EmbeddedPromptResource.RequiredString(prompt.RootElement, "version"),
            SystemText = EmbeddedPromptResource.RequiredString(prompt.RootElement.GetProperty("how"), "system"),
            SchemaJson = EmbeddedPromptResource.Read(SchemaResource),
        };
    }
}
