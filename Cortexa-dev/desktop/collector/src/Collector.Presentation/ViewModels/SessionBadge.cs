using Collector.Application.Auth;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public enum BadgeKind
{
    SignedIn,
    SignedOut,
    Expired,
}

public sealed record SessionBadge
{
    public required BadgeKind Kind { get; init; }

    public required string Glyph { get; init; }

    public required string Line1 { get; init; }

    public string? Line2 { get; init; }

    public required string ItemStatus { get; init; }

    public bool IsSigningOut { get; init; }

    public bool ShowSignOut => Kind == BadgeKind.SignedIn;

    public bool ShowSignInAction => Kind != BadgeKind.SignedIn;

    public bool CanSignOut => !IsSigningOut;

    public string SignInActionText =>
        Kind == BadgeKind.Expired ? ShellStrings.SignInAgain : ShellStrings.SignIn;

    public static SessionBadge For(SessionState state, string? email, bool signingOut) => state switch
    {
        SessionState.SignedIn => SignedIn(email ?? string.Empty, signingOut),
        SessionState.Expired => Expired(email),
        _ => SignedOut(),
    };

    private static SessionBadge SignedIn(string email, bool signingOut) =>
        new()
        {
            Kind = BadgeKind.SignedIn,
            Glyph = Glyphs.Success,
            Line1 = email,
            Line2 = signingOut ? ShellStrings.SigningOut : ShellStrings.SignedIn,
            ItemStatus = ShellStrings.SignedInAs(email),
            IsSigningOut = signingOut,
        };

    private static SessionBadge Expired(string? email) =>
        new()
        {
            Kind = BadgeKind.Expired,
            Glyph = Glyphs.Warning,
            Line1 = string.IsNullOrEmpty(email) ? ShellStrings.SessionExpired : email,
            Line2 = ShellStrings.SessionExpired,
            ItemStatus = ShellStrings.SessionExpired,
        };

    private static SessionBadge SignedOut() =>
        new()
        {
            Kind = BadgeKind.SignedOut,
            Glyph = Glyphs.SignIn,
            Line1 = ShellStrings.NotSignedIn,
            ItemStatus = ShellStrings.NotSignedIn,
        };
}
