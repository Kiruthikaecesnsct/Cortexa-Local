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
    ];

    public static string Title(CollectorProvider provider) =>
        Cards.FirstOrDefault(card => card.Provider == provider)?.Title ?? provider.ToString();

    public static string Chip(CollectorProvider provider, ProviderReadinessState state) => state switch
    {
        ProviderReadinessState.Ready => provider == CollectorProvider.Bedrock ? SettingsStrings.BedrockConnectedChip : SettingsStrings.ChipKeySet,
        ProviderReadinessState.Missing => provider == CollectorProvider.Bedrock ? SettingsStrings.BedrockNotConnectedChip : SettingsStrings.ChipKeyNotSet,
        ProviderReadinessState.Unknown => SettingsStrings.ChipUnknown,
        _ => SettingsStrings.ChipChecking,
    };
}
