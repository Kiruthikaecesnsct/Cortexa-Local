namespace Collector.Presentation.Resources;

public static class SignInStrings
{
    public const string Heading = "Sign in to Cortexa";
    public const string Intro = "Use your Cortexa account. Your files stay on this computer.";
    public const string HeroTitle = "Turn research into patent opportunities";
    public const string HeroBody = "Collect knowledge from papers, theses and code. Cortexa finds the inventions inside.";
    public const string HeroPointLocal = "Your files never leave this computer.";
    public const string HeroPointKeys = "Knowledge is extracted with your own AI keys.";
    public const string HeroPointTracked = "Every upload is tracked through the pipeline.";
    public const string EmailLabel = "_Email";
    public const string EmailName = "Email";
    public const string PasswordLabel = "_Password";
    public const string PasswordName = "Password";
    public const string SignInButton = "_Sign in";
    public const string SignInBusy = "Signing in…";
    public const string SignInName = "Sign in";
    public const string SignInBusyName = "Signing in";
    public const string Cancel = "Cancel";
    public const string CancelName = "Cancel sign-in";
    public const string Change = "Change";
    public const string ChangeName = "Change gateway URL in Settings";
    public const string OpenSettings = "Open Settings";
    public const string DismissName = "Dismiss message";
    public const string ProgressName = "Signing in";

    public const string GatewayNotSetTitle = "Gateway not set";
    public const string GatewayNotSetMessage = "Set the gateway URL in Settings before you sign in.";

    public const string ExpiredTitle = "Session expired.";
    public const string ExpiredMessage = "Sign in again.";

    public const string EmailEmpty = "Enter your email address.";
    public const string EmailTooLong = "Email must be 254 characters or fewer.";
    public const string EmailFormat = "Enter a valid email address, like name@company.com.";
    public const string PasswordEmpty = "Enter your password.";
    public const string PasswordTooLong = "Password must be 128 characters or fewer.";
    public const string FieldUnknown = "Check this field and try again.";

    public const string RejectedTitle = "Sign-in details not accepted.";
    public const string RejectedMessage = "Cortexa could not accept the email or password format. Check both and try again.";
    public const string InvalidCredentialsTitle = "Email or password is incorrect.";
    public const string InvalidCredentialsMessage = "Check both and try again.";
    public const string ForbiddenTitle = "This account can't sign in here.";
    public const string ForbiddenMessage = "Your Cortexa account does not have access. Contact your Cortexa administrator.";
    public const string LockedTitle = "Account locked.";
    public const string LockedUntimed = "Too many failed sign-in attempts. Wait a few minutes, then try again.";
    public const string RateLimitedTitle = "Too many attempts.";
    public const string RateLimitedUntimed = "Wait a minute, then try again.";
    public const string UnreachableTitle = "Can't reach Cortexa.";
    public const string UnexpectedTitle = "Sign-in didn't work.";

    public const string UnexpectedMessage =
        "Cortexa sent a response the app didn't expect. Try again. If it keeps happening, check the gateway URL in Settings.";

    public const string RetryReadyTitle = "You can try again now.";
    public const string LessThanAMinute = "less than a minute";

    public static string GatewayLine(string host) => $"Gateway: {host}";

    public static string UnreachableMessage(string host) =>
        $"Check your network connection and the gateway URL ({host}).";

    public static string LockedTimed(string remaining) =>
        $"Too many failed sign-in attempts. Try again in {remaining}.";

    public static string RateLimitedTimed(string remaining) => $"Try again in {remaining}.";

    public static string LockedAutomation(string approx) =>
        $"{LockedTitle} Too many failed sign-in attempts. Try again in {approx}.";

    public static string RateLimitedAutomation(string approx) => $"{RateLimitedTitle} Try again in {approx}.";

    public static string Approximately(int minutes) =>
        minutes == 1 ? "about 1 minute" : $"about {minutes} minutes";
}
