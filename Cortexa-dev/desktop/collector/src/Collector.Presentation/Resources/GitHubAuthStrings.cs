namespace Collector.Presentation.Resources;

public static class GitHubAuthStrings
{
    public const string AuthPickerName = "Sign-in method";
    public const string SignInSectionTitle = "Sign in";
    public const string DeviceMethodLabel = "GitHub account";
    public const string DeviceMethodName = "GitHub account";
    public const string AppMethodLabel = "GitHub App";
    public const string AppMethodName = "GitHub App";
    public const string PatMethodLabel = "Access token";
    public const string PatMethodName = "Access token";

    public const string DeviceMethodHint =
        "Sign in with your GitHub account. You see every repository your account can read.";

    public const string AppMethodHint =
        "Sign in through the GitHub App. You see only the repositories the app is installed on.";

    public const string PatMethodHint = "Paste a personal access token. It's kept in memory for this session only.";

    public const string DeviceMethodUnavailable =
        "Not set up in this build. Ask your admin to add the GitHub client ID.";

    public const string AppMethodUnavailable =
        "Not set up in this build. Ask your admin to add the GitHub App client ID.";

    public const string BothMethodsUnavailableNote =
        "GitHub sign-in isn't set up in this build. Ask your admin to add the GitHub client IDs. You can still use an access token.";

    public const string DeviceMethodUnavailableNote =
        "GitHub account sign-in isn't set up in this build. Ask your admin to add the GitHub client ID.";

    public const string AppMethodUnavailableNote =
        "GitHub App sign-in isn't set up in this build. Ask your admin to add the GitHub App client ID.";

    public const string OAuthNote =
        "You'll get a short code to enter on github.com. There's no token to paste. Your sign-in is saved in Windows Credential Manager until you sign out.";

    public const string SignIn = "_Sign in with GitHub";
    public const string SignInName = "Sign in with GitHub";
    public const string OAuthConnectPromptText = "Enter your organization URL, then select Sign in with GitHub.";
    public const string WaitingPromptText = "Enter the code on GitHub to finish signing in.";
    public const string SignedInConnectPromptText = "Enter your organization URL, then select Connect and find repositories.";

    public const string RequestingCode = "Getting a code from GitHub…";
    public const string CodePanelTitle = "Enter this code on GitHub";
    public const string CodeStepCopy = "Copy the code.";
    public const string CodeStepOpen = "Open github.com/login/device and paste it.";
    public const string CodeStepApprove = "Approve access. This page continues on its own.";
    public const string CopyCode = "_Copy code";
    public const string CopyCodeName = "Copy the code";
    public const string Copied = "Copied";
    public const string CopiedAnnouncement = "Code copied.";
    public const string CopyFailed = "Couldn't copy the code. Select it and press Ctrl+C.";
    public const string OpenBrowser = "_Open browser";
    public const string OpenBrowserName = "Open github.com/login/device in your browser";

    public const string HostMismatch =
        "GitHub sent an unexpected sign-in address, so it wasn't opened. Cancel and try again.";

    public const string Waiting = "Waiting for you to approve on GitHub…";
    public const string CancelSignIn = "Cancel";
    public const string CancelSignInName = "Cancel GitHub sign-in";

    public const string ExpiredTitle = "This code expired.";
    public const string DeniedTitle = "GitHub sign-in was denied.";

    public const string DeniedMessage =
        "Access wasn't approved on GitHub, so nothing was saved. Get a new code to try again.";

    public const string FailedTitle = "Couldn't sign in to GitHub.";
    public const string FailedMessage = "Check your network connection, then try again. Nothing was saved.";
    public const string DisabledTitle = "Device sign-in is turned off on GitHub.";

    public const string DisabledMessage =
        "The GitHub app behind this sign-in doesn't allow device codes. Use an access token, or ask your admin to turn on device flow.";

    public const string Canceled = "Sign-in canceled. Nothing was saved.";
    public const string GetNewCode = "Get a new code";
    public const string TryAgain = "Try again";
    public const string UsePat = "Use an access token";
    public const string UsePatName = "Switch to signing in with an access token";

    public const string SignedInDevice = "Signed in to GitHub with your GitHub account";
    public const string SignedInApp = "Signed in to GitHub with the GitHub App";
    public const string SignedInHint = "You're signed in. Sign out to switch method.";
    public const string SignedInSaved = "Your sign-in is saved on this computer. Sign out to remove it.";
    public const string SignOut = "Sign out of GitHub";
    public const string SignOutName = "Sign out of GitHub and remove the saved sign-in";
    public const string SigningOut = "Signing out…";
    public const string SignedOut = "Signed out of GitHub. Your saved sign-in was removed.";
    public const string SignOutFailed = "Couldn't remove the saved sign-in. Try again.";
    public const string DisconnectOAuthName = "Disconnect from this organization. You stay signed in to GitHub.";

    public const string ExpiredSignInTitle = "Your GitHub sign-in expired.";
    public const string ExpiredSignInMessage = "Sign in again to keep fetching repositories.";
    public const string SignInAgain = "Sign in again";
    public const string SignInAgainName = "Go back and sign in to GitHub again";

    public static string OrgBlockedTitle(string organization) =>
        $"GitHub won't let your sign-in read {organization}.";

    public const string OrgBlockedMessage =
        "An org owner may need to approve this app, or your account may not meet the org's rules, such as secure two-factor sign-in. Ask an org owner, or sign out and use an access token.";
    public const string OAuthConnectingMessage = "Finding every repository your GitHub sign-in can see.";

    public static string BrowserFailed(string url) =>
        $"Couldn't open your browser. Go to {url} and enter the code.";

    public static string ExpiresIn(string mmss) => $"Code expires in {mmss}";

    public static string ExpiresAnnouncement(string approx) => $"The code expires in {approx}.";

    public static string UserCodeName(string spelled) => $"Your code, {spelled}";

    public static string ExpiredMessage(int minutes) =>
        $"Codes last {minutes} minutes. Nothing was saved. Get a new code to try again.";

    public static string EmptyGitHubApp(string organization) =>
        $"The GitHub App isn't installed on {organization}, or it has no repositories granted. Ask an org owner to install it, then connect again.";
}
