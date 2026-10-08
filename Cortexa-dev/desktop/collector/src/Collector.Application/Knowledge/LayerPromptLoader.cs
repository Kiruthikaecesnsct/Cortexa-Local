using System.Text.Json;

namespace Collector.Application.Knowledge;

public static class LayerPromptLoader
{
    private const string PromptResource = "layer-prompt.v1.json";
    private const string SchemaResource = "layer-output.schema.json";

    public static LayerPrompt Load()
    {
        using var prompt = JsonDocument.Parse(EmbeddedPromptResource.Read(PromptResource));
        return new LayerPrompt
        {
            Version = EmbeddedPromptResource.RequiredString(prompt.RootElement, "version"),
            SystemText = EmbeddedPromptResource.RequiredString(prompt.RootElement.GetProperty("how"), "system"),
            SchemaJson = EmbeddedPromptResource.Read(SchemaResource),
            Guard = KnowledgePromptLoader.LoadGuard(),
        };
    }
}
