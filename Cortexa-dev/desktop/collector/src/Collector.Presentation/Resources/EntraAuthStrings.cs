namespace Collector.Presentation.Resources;

public static class EntraAuthStrings
{
    public const string MethodLabel = "Work or school account";
    public const string MethodName = "Work or school account";
    public const string MethodHint = "Sign in with your work or school account. No token to paste.";
    public const string MethodUnavailable = "Not set up in this build. Ask your admin to add the Entra client ID.";

    public const string UnavailableNote =
        "Microsoft sign-in isn't set up in this build. Ask your admin to add the Entra client ID. You can still use an access token.";

    public const string SavedNote = "Your sign-in is saved in Windows Credential Manager until you sign out.";

    public const string SignIn = "_Sign in with Microsoft";
    public const string SignInName = "Sign in with Microsoft";
    public const string CodeInstead = "Sign in with a code instead";
    public const string CodeInsteadName = "Sign in with a one-time code in your browser";
    public const string OrgPromptText = "Enter your organization URL, then select Sign in with Microsoft.";
    public const string WaitingPromptText = "Finish signing in at Microsoft.";
    public const string SignedInConnectPromptText = "Enter your organization URL, then select Connect and find repositories.";

    public const string SigningIn = "Finish signing in in the Microsoft window…";
    public const string SigningInAnnouncement = "Signing in with Microsoft. Finish in the Microsoft window.";
    public const string CodePanelTitle = "Enter this code at Microsoft";
    public const string CodeStepCopy = "Copy the code.";
    public const string CodeStepApprove = "Sign in and approve. This page continues on its own.";
    public const string CopyCode = "_Copy code";
    public const string OpenBrowser = "_Open browser";
    public const string Waiting = "Waiting for you to sign in at Microsoft…";
    public const string CancelSignInName = "Cancel Microsoft sign-in";

    public const string UnsafeUrl =
        "Microsoft sent a sign-in address that isn't secure, so it wasn't opened. Cancel and try again.";

    public const string Canceled = "Sign-in canceled. Nothing was saved.";

    public const string ExpiredTitle = "This code expired.";
    public const string FailedTitle = "Couldn't sign in with Microsoft.";
    public const string FailedMessage = "Check your network connection, then try again. Nothing was saved.";
    public const string UserCanceledTitle = "Sign-in wasn't finished.";
    public const string UserCanceledMessage = "The Microsoft window was closed before you signed in. Nothing was saved.";

    public const string SignedInGeneric = "Signed in with Microsoft";
    public const string SignedInHint = "You're signed in. Sign out to switch method.";
    public const string SignOut = "Sign out";
    public const string SignOutName = "Sign out of Microsoft and remove the saved sign-in";
    public const string SigningOut = "Signing out…";
    public const string SignedOut = "Signed out of Microsoft. Your saved sign-in was removed.";
    public const string SignOutFailed = "Couldn't remove the saved sign-in. Try again.";
    public const string DisconnectName = "Disconnect from this organization. You stay signed in to Microsoft.";
    public const string ConnectingMessage = "Finding every repository your Microsoft sign-in can see.";

    public const string NoAccessTitle = "Your Microsoft account can't open this organization.";
    public const string ExpiredSignInTitle = "Your Microsoft sign-in expired.";
    public const string SignInAgainName = "Go back and sign in with Microsoft again";
    public const string UsePat = "Use a personal access token";
    public const string UsePatName = "Switch to signing in with a personal access token";

    public static string SignedInAs(string account) => $"Signed in as {account}";

    public static string CodeStepOpen(string url) => $"Open {url} and paste it.";

    public static string OpenBrowserName(string url) => $"Open {url} in your browser";
}
