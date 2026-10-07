namespace Collector.Presentation.Resources;

public static class ShellStrings
{
    public const string AppName = "Cortexa Collector";
    public const string SignIn = "Sign in";
    public const string Settings = "Settings";
    public const string SignedIn = "Signed in";
    public const string NotSignedIn = "Not signed in";
    public const string SessionExpired = "Session expired";
    public const string SignOut = "Sign out";
    public const string SigningOut = "Signing out…";
    public const string SignInAgain = "Sign in again";
    public const string SignInRequired = "Sign in to use this.";
    public const string RequiresSignInHelp = "Requires sign-in";
    public const string SessionAutomationName = "Session";
    public const string MainNavName = "Screens";
    public const string FooterNavName = "App";

    public const string UnhandledError =
        "Something went wrong and the app could not finish that action. Details were saved to the log file. If the app keeps misbehaving, restart it.";

    public static string SignedInAs(string email) => $"Signed in as {email}";
}
