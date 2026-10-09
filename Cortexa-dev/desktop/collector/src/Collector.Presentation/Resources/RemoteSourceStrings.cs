namespace Collector.Presentation.Resources;

public static class RemoteSourceStrings
{
    public const string GitHubTokensUrl = "https://github.com/settings/tokens";

    public const string SourceGroupName = "Source";
    public const string LocalLabel = "_Local files";
    public const string GitHubLabel = "_GitHub";
    public const string AzureDevOpsLabel = "Azure _DevOps";
    public const string LocalName = "Local files";
    public const string GitHubName = "GitHub";
    public const string AzureDevOpsName = "Azure DevOps";

    public const string RepositoryCard = "Repository";
    public const string OrganizationLabel = "_Organization";
    public const string OrganizationName = "Organization";
    public const string OrganizationHelper = "The name after dev.azure.com/. Saved on this computer.";
    public const string OrganizationEmpty = "Enter the organization name.";
    public const string LoadRepositories = "Load _repositories";
    public const string LoadRepositoriesName = "Load repositories";
    public const string LoadingRepositories = "Loading repositories…";
    public const string Reload = "Reload repositories";
    public const string SearchLabel = "_Search repositories";
    public const string SearchName = "Search repositories";
    public const string SearchPlaceholder = "Filter by name";
    public const string RepositoriesName = "Repositories";
    public const string EmptyTitle = "No repositories to show";

    public const string EmptyGitHub =
        "This token can't see any repositories. Check its scopes and which repositories it can access, then reload.";

    public const string ClearSearch = "Clear search";
    public const string PrivateTag = "Private";
    public const string BranchLabel = "_Branch";
    public const string BranchName = "Branch";
    public const string LoadingBranches = "Loading branches…";
    public const string DefaultSuffix = " (default)";
    public const string Fetch = "_Fetch files";
    public const string FetchName = "Fetch files";
    public const string FetchHelp = "Pick a repository first.";

    public const string FetchNote =
        "Downloads text and code files from the branch. Files already cached on this computer aren't downloaded again.";

    public const string ReadingTree = "Reading the file list…";
    public const string Cancel = "Cancel";
    public const string CancelName = "Cancel repository fetch";
    public const string ProgressName = "Repository fetch progress";
    public const string Resumed = "Fetch resumed.";
    public const string CanceledTitle = "Fetch canceled.";

    public const string CanceledMessage =
        "Nothing was added. Files already downloaded stay in the cache, so the next fetch is faster.";

    public const string ChangeRepository = "Change repository";
    public const string FetchAgain = "Fetch again";
    public const string OpenSettings = "Open Settings";
    public const string TryAgain = "Try again";
    public const string AuthMessage = "It may have expired or been revoked. Replace it in Settings, then try again.";
    public const string DeniedTitle = "Your token can't read this repository.";
    public const string OpenGitHubTokens = "Open GitHub token settings";
    public const string OpenGitHubTokensName = "Open GitHub token settings in your browser";
    public const string NotFoundTitle = "Repository or branch not found.";
    public const string EmptyRepositoryTitle = "This repository is empty.";
    public const string TooLargeTitle = "This repository is too large to fetch.";
    public const string UpstreamMessage = "The service returned an error. Try again in a few minutes.";
    public const string TruncatedTitle = "Some files weren't fetched.";
    public const string NoFilesTitle = "No supported files found.";
    public const string SwitchToLocalName = "Switch to Local files";

    public static string OrganizationNotFound(string organization) =>
        $"Couldn't find the organization \"{organization}\". Check the spelling, or check that your token was created for it.";

    public static string EmptyAzureDevOps(string organization) =>
        $"This token can't see any repositories in {organization}. Check that it has Code (Read) for this organization, then reload.";

    public static string Showing(int visible, int total) => $"Showing {visible} of {total}";

    public static string NoMatch(string query) => $"No repositories match \"{query}\".";

    public static string OverLimitTag(string limit) => $"Over {limit}";

    public static string Commit(string sha) => $"Commit {sha}";

    public static string TooBigHelp(string size, string limit) =>
        $"This repository is about {size}. The fetch limit is {limit}.";

    public static string FetchingTitle(string repository, string branch) => $"Fetching {repository} @ {branch}";

    public static string Progress(int done, int total) => $"Fetched {done} of {total} files";

    public static string Paused(int done, int total) => $"Paused. Fetched {done} of {total} files";

    public static string Summary(int downloaded, int cached, int filtered, int tooLarge) =>
        $"Fetched {downloaded + cached} files · {cached} from cache · {filtered + tooLarge} skipped ({filtered} by filter, {tooLarge} too large)";

    public static string RateTitle(string provider, bool isGitHub) =>
        isGitHub ? "GitHub rate limit reached." : $"{provider} is limiting requests.";

    public static string RateMessage(string remaining) => $"Resuming in {remaining}. The fetch continues on its own.";

    public static string RateAnnouncement(string title, string approximate) => $"{title} Resuming in {approximate}.";

    public static string RateLimitedMessage(string approximate) => $"Try again in {approximate}.";

    public static string MissingTitle(string provider) => $"Add your {provider} token in Settings.";

    public static string MissingMessage(string provider) =>
        $"Listing and fetching {provider} repositories uses your own personal access token.";

    public static string OpenSettingsAddName(string provider) => $"Open Settings to add your {provider} token";

    public static string OpenSettingsReplaceName(string provider) => $"Open Settings to replace your {provider} token";

    public static string AuthTitle(string provider) => $"{provider} didn't accept your token.";

    public static string DeniedMessage(string repository, bool isGitHub) =>
        isGitHub
            ? $"Give the token Contents (Read) access to {repository}, or pick another repository."
            : $"Give the token Code (Read) access to {repository}, or pick another repository.";

    public static string SsoTitle(string organization) => $"Authorize your token for {organization} single sign-on.";

    public static string SsoMessage(string organization) =>
        $"{organization} uses SAML single sign-on. On GitHub, open your token settings, choose Configure SSO, and authorize {organization}. Then try again.";

    public static string NotFoundMessage(string repository, string branch) =>
        $"{repository} @ {branch} may have been renamed, moved, or deleted. Reload the list and pick it again.";

    public static string EmptyRepositoryMessage(string repository, string branch) =>
        $"{repository} has no files on {branch}. Pick another repository or branch.";

    public static string TooLargeMessage(string repository, string size, string limit) =>
        $"{repository} is {size}. The limit is {limit}. Clone it yourself, then use Local files with the folders you need.";

    public static string UpstreamTitle(string provider) => $"{provider} isn't responding.";

    public static string TruncatedMessage(string provider, int fetched) =>
        $"{provider} returned only part of the file list because the repository is very large. {fetched} files were fetched. Use Local files for anything missing.";

    public static string NoFilesMessage(string repository, string branch) =>
        $"{repository} has no text or code files the collector can read on {branch}.";

    public static string RepositoryAutomationName(string name, bool isPrivate, string branch, string size, string? overLimit) =>
        $"{name}, {(isPrivate ? "private, " : string.Empty)}default branch {branch}, {size}{(overLimit is null ? string.Empty : $", over the {overLimit} fetch limit")}";

    public const string SshLabel = "_SSH";
    public const string SshName = "SSH";

    public const string SshHostLabel = "_Host";
    public const string SshHostName = "Host";
    public const string SshHostPlaceholder = "example.com or 192.0.2.1";

    public const string SshPortLabel = "_Port";
    public const string SshPortName = "Port";
    public const string SshPortPlaceholder = "22";

    public const string SshUsernameLabel = "_Username";
    public const string SshUsernameName = "Username";
    public const string SshUsernamePlaceholder = "Username";

    public const string SshKeyFileLabel = "_Key file";
    public const string SshKeyFileName = "Key file";
    public const string SshKeyFilePlaceholder = "Choose a private key file";
    public const string SshBrowseKeyFile = "_Browse…";
    public const string SshBrowseKeyFileName = "Browse for a private key file";
    public const string SshKeyFileDialogTitle = "Choose a private key file";
    public const string SshKeyFileDialogFilter = "All files (*.*)|*.*";

    public const string SshFingerprintLabel = "_Fingerprint";
    public const string SshFingerprintName = "Fingerprint";
    public const string SshFingerprintPlaceholder = "SHA256:…";

    public const string SshConnect = "_Connect";
    public const string SshConnectName = "Connect";
    public const string SshConnectHelp = "Fill in the host, username, and key file first.";

    public const string SshConnecting = "Connecting…";

    public const string SshInvalidPort = "Enter a port between 1 and 65535.";
    public const string SshKeyFileNotFound = "The key file couldn't be found. Choose it again.";

    public const string SshFingerprintMismatchTitle = "The server's fingerprint doesn't match.";

    public const string SshFingerprintMismatchMessage =
        "This may mean the server changed or someone is intercepting the connection. Verify the fingerprint with the server administrator before continuing.";

    public const string SshAuthTitle = "The server didn't accept your key.";
    public const string SshAuthMessage = "Check the key file and its passphrase, then try again.";

    public const string SshDeniedTitle = "You don't have permission to read these files.";
    public const string SshDeniedMessage = "Ask the server administrator for read access, then try again.";

    public const string SshConnectionFailedTitle = "Couldn't connect to the server.";
    public const string SshConnectionFailedMessage = "Check the host, port, and your network connection, then try again.";

    public const string CortexaLabel = "Cortexa sa_ved";
    public const string CortexaName = "Cortexa saved";
    public const string EmptyCortexa = "No saved repositories. Save one from the Cortexa web app.";
    public const string CortexaAuthTitle = "Your Cortexa session expired.";
    public const string CortexaAuthMessage = "Sign in again.";
    public const string CortexaDeniedTitle = "Cortexa access denied.";
    public const string CortexaDeniedMessage = "Your Cortexa account does not have permission to read saved repositories.";
    public const string UpstreamGitHubTag = "GitHub";
    public const string UpstreamAzureTag = "Azure DevOps";
    public const string CortexaBranchHelper = "Saved branch. To fetch a different branch, save it in the Cortexa web app.";

    public static string CortexaBranchName(string branch) => $"Branch, {branch}";

    public static string CortexaRepositoryAutomationName(CortexaRowDescription row) =>
        $"{row.FullName} @ {row.Branch}{(row.Upstream is null ? string.Empty : $", from {row.Upstream}")}, {row.Size}"
        + (row.OverLimit is null ? string.Empty : $", over the {row.OverLimit} fetch limit");
}

public sealed record CortexaRowDescription(string FullName, string Branch, string? Upstream, string Size, string? OverLimit);
