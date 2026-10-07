using Collector.Application.Auth;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public enum CountdownKind
{
    Locked,
    RateLimited,
}

public static class SignInFocusKeys
{
    public const string Email = "Email";
    public const string Password = "Password";
    public const string BannerAction = "BannerAction";
    public const string Banner = "Banner";
    public const string Cancel = "Cancel";
}

public readonly record struct SignInContext(string Email, string Password, string? GatewayHost)
{
    public override string ToString() => $"{nameof(SignInContext)} {{ {nameof(GatewayHost)} = {GatewayHost} }}";
}

public sealed record SignInPresentation
{
    public bool Succeeded { get; init; }

    public string? EmailError { get; init; }

    public string? PasswordError { get; init; }

    public BannerContent? Banner { get; init; }

    public bool OffersSettings { get; init; }

    public bool ClearPassword { get; init; } = true;

    public string FocusKey { get; init; } = SignInFocusKeys.Email;

    public CountdownKind? Countdown { get; init; }

    public TimeSpan? RetryAfter { get; init; }
}

public static class SignInOutcomePresenter
{
    public static SignInPresentation Present(SignInOutcome outcome, SignInContext context) => outcome switch
    {
        SignInOutcome.Success => new SignInPresentation { Succeeded = true },
        SignInOutcome.InvalidInput invalid => FromInvalidInput(invalid, context),
        SignInOutcome.InvalidCredentials => InvalidCredentials(),
        SignInOutcome.Forbidden => Forbidden(),
        SignInOutcome.AccountLocked locked => Locked(locked.RetryAfter),
        SignInOutcome.RateLimited limited => RateLimited(limited.RetryAfter),
        SignInOutcome.Unreachable => Unreachable(context.GatewayHost),
        _ => Unexpected(),
    };

    public static string CountdownMessage(CountdownKind kind, TimeSpan remaining)
    {
        var text = CountdownFormatter.Format(remaining);
        return kind == CountdownKind.Locked ? SignInStrings.LockedTimed(text) : SignInStrings.RateLimitedTimed(text);
    }

    private static SignInPresentation FromInvalidInput(SignInOutcome.InvalidInput invalid, SignInContext context) =>
        invalid.Field switch
        {
            SignInField.Email => new SignInPresentation
            {
                EmailError = EmailReason(context.Email),
                ClearPassword = false,
                FocusKey = SignInFocusKeys.Email,
            },
            SignInField.Password => new SignInPresentation
            {
                PasswordError = PasswordReason(context.Password),
                FocusKey = SignInFocusKeys.Password,
            },
            _ => ErrorBanner(SignInStrings.RejectedTitle, SignInStrings.RejectedMessage, SignInFocusKeys.Email),
        };

    private static string EmailReason(string email)
    {
        var trimmed = email.Trim();
        if (trimmed.Length == 0)
        {
            return SignInStrings.EmailEmpty;
        }

        return trimmed.Length > SignInRules.MaxEmailLength ? SignInStrings.EmailTooLong : SignInStrings.EmailFormat;
    }

    private static string PasswordReason(string password)
    {
        if (password.Length == 0)
        {
            return SignInStrings.PasswordEmpty;
        }

        return password.Length > SignInRules.MaxPasswordLength
            ? SignInStrings.PasswordTooLong
            : SignInStrings.FieldUnknown;
    }

    private static SignInPresentation InvalidCredentials() =>
        ErrorBanner(
            SignInStrings.InvalidCredentialsTitle,
            SignInStrings.InvalidCredentialsMessage,
            SignInFocusKeys.Password);

    private static SignInPresentation Forbidden() =>
        ErrorBanner(SignInStrings.ForbiddenTitle, SignInStrings.ForbiddenMessage, SignInFocusKeys.Email);

    private static SignInPresentation Unreachable(string? host) =>
        ErrorBanner(
            SignInStrings.UnreachableTitle,
            SignInStrings.UnreachableMessage(host ?? string.Empty),
            SignInFocusKeys.BannerAction) with
        {
            OffersSettings = true,
        };

    private static SignInPresentation Unexpected() =>
        ErrorBanner(SignInStrings.UnexpectedTitle, SignInStrings.UnexpectedMessage, SignInFocusKeys.BannerAction) with
        {
            OffersSettings = true,
        };

    private static SignInPresentation Locked(TimeSpan? retryAfter)
    {
        if (retryAfter is not { } wait || wait <= TimeSpan.Zero)
        {
            return WarningBanner(SignInStrings.LockedTitle, SignInStrings.LockedUntimed, Glyphs.Lock);
        }

        return Timed(
            CountdownKind.Locked,
            wait,
            SignInStrings.LockedTitle,
            SignInStrings.LockedAutomation(CountdownFormatter.Approximate(wait)),
            Glyphs.Lock);
    }

    private static SignInPresentation RateLimited(TimeSpan? retryAfter)
    {
        if (retryAfter is not { } wait || wait <= TimeSpan.Zero)
        {
            return WarningBanner(SignInStrings.RateLimitedTitle, SignInStrings.RateLimitedUntimed, null);
        }

        return Timed(
            CountdownKind.RateLimited,
            wait,
            SignInStrings.RateLimitedTitle,
            SignInStrings.RateLimitedAutomation(CountdownFormatter.Approximate(wait)),
            null);
    }

    private static SignInPresentation Timed(
        CountdownKind kind,
        TimeSpan wait,
        string title,
        string automationName,
        string? glyph) =>
        new()
        {
            Banner = new BannerContent
            {
                Severity = BannerSeverity.Warning,
                Title = title,
                Message = CountdownMessage(kind, wait),
                Glyph = glyph,
                AutomationName = automationName,
            },
            Countdown = kind,
            RetryAfter = wait,
            FocusKey = SignInFocusKeys.Banner,
        };

    private static SignInPresentation ErrorBanner(string title, string message, string focusKey) =>
        new()
        {
            Banner = new BannerContent { Severity = BannerSeverity.Error, Title = title, Message = message },
            FocusKey = focusKey,
        };

    private static SignInPresentation WarningBanner(string title, string message, string? glyph) =>
        new()
        {
            Banner = new BannerContent
            {
                Severity = BannerSeverity.Warning,
                Title = title,
                Message = message,
                Glyph = glyph,
            },
            FocusKey = SignInFocusKeys.Banner,
        };
}
