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
    private const int MaxInitials = 2;
    private static readonly char[] InitialSeparators = ['.', '_', '-', '+'];

    public required BadgeKind Kind { get; init; }

    public required string Glyph { get; init; }

    public required string Line1 { get; init; }

    public string? Line2 { get; init; }

    public required string ItemStatus { get; init; }

    public bool IsSigningOut { get; init; }

    public bool ShowSignOut => Kind == BadgeKind.SignedIn;

    public bool ShowSignInAction => Kind != BadgeKind.SignedIn;

    public bool CanSignOut => !IsSigningOut;

    public string Initials => Kind == BadgeKind.SignedIn ? InitialsFrom(Line1) : string.Empty;

    public bool HasInitials => Initials.Length > 0;

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

    private static string InitialsFrom(string email)
    {
        var localPart = email.Split('@')[0];
        var letters = localPart
            .Split(InitialSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.FirstOrDefault(char.IsLetterOrDigit))
            .Where(letter => letter != default)
            .Take(MaxInitials);
        return string.Concat(letters).ToUpperInvariant();
    }

    private static SessionBadge SignedOut() =>
        new()
        {
            Kind = BadgeKind.SignedOut,
            Glyph = Glyphs.SignIn,
            Line1 = ShellStrings.NotSignedIn,
            ItemStatus = ShellStrings.NotSignedIn,
        };
}
