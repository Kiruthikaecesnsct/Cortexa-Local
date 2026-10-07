namespace Collector.Application.Knowledge;

public sealed record GuardText
{
    public required string Header { get; init; }

    public required string BeginMarker { get; init; }

    public required string EndMarker { get; init; }

    public required IReadOnlyList<string> SecurityLines { get; init; }

    public required string LineSeparator { get; init; }

    public required string ZeroWidthSpace { get; init; }

    public required IReadOnlyList<string> EscapeTokens { get; init; }

    public required IReadOnlyList<string> HeaderMarkers { get; init; }

    public required IReadOnlyList<ProseSubstitution> ProseSubstitutions { get; init; }
}

public sealed record ProseSubstitution(string From, string To);
