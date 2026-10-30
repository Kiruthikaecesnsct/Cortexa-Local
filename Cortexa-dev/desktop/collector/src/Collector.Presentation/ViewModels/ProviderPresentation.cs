using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;

namespace Collector.Presentation.ViewModels;

public sealed record ProviderCardContent(CollectorProvider Provider, string Title, string Description, string Monogram);

public static class ProviderPresentation
{
    public static IReadOnlyList<ProviderCardContent> Cards { get; } =
    [
        new(CollectorProvider.Claude, SettingsStrings.ProviderClaudeTitle, SettingsStrings.ProviderClaudeDescription, "C"),
        new(CollectorProvider.Gemini, SettingsStrings.ProviderGeminiTitle, SettingsStrings.ProviderGeminiDescription, "G"),
        new(CollectorProvider.Bedrock, SettingsStrings.ProviderBedrockTitle, SettingsStrings.ProviderBedrockDescription, "B"),
        new(CollectorProvider.Groq, SettingsStrings.ProviderGroqTitle, SettingsStrings.ProviderGroqDescription, "Q"),
        new(CollectorProvider.Vertex, SettingsStrings.ProviderVertexTitle, SettingsStrings.ProviderVertexDescription, "V"),
    ];

    public static string Title(CollectorProvider provider) =>
        Cards.FirstOrDefault(card => card.Provider == provider)?.Title ?? provider.ToString();

    public static string Chip(CollectorProvider provider, ProviderReadinessState state) => state switch
    {
        ProviderReadinessState.Ready => ReadyChip(provider),
        ProviderReadinessState.Missing => MissingChip(provider),
        ProviderReadinessState.Unknown => SettingsStrings.ChipUnknown,
        _ => SettingsStrings.ChipChecking,
    };

    private static string ReadyChip(CollectorProvider provider) => provider switch
    {
        CollectorProvider.Bedrock => SettingsStrings.BedrockConnectedChip,
        CollectorProvider.Vertex => SettingsStrings.VertexReadyChip,
        _ => SettingsStrings.ChipKeySet,
    };

    private static string MissingChip(CollectorProvider provider) => provider switch
    {
        CollectorProvider.Bedrock => SettingsStrings.BedrockNotConnectedChip,
        CollectorProvider.Vertex => SettingsStrings.VertexNotReadyChip,
        _ => SettingsStrings.ChipKeyNotSet,
    };
}
