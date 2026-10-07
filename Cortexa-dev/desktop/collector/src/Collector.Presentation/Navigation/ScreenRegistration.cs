namespace Collector.Presentation.Navigation;

public enum NavPlacement
{
    Main,
    Footer,
}

public sealed record ScreenRegistration
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required Type ViewModelType { get; init; }

    public required bool RequiresSignIn { get; init; }

    public required string Glyph { get; init; }

    public required NavPlacement Placement { get; init; }

    public required int Order { get; init; }
}

public static class ScreenKeys
{
    public const string SignIn = "signin";
    public const string Settings = "settings";
    public const string Extract = "extract";
    public const string Review = "review";
}
