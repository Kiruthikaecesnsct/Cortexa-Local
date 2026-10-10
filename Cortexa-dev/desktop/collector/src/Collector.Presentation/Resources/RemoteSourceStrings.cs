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
    public const string LoadingRepositories = "Loading repositories…";
    public const string Reload = "Reload repositories";
    public const string SearchLabel = "_Search repositories";
    public const string SearchName = "Search repositories";
    public const string SearchPlaceholder = "Search by name or description";
    public const string RepositoriesName = "Repositories";
    public const string EmptyTitle = "No repositories to show";

    public const string EmptyGitHub =
        "This token can't see any repositories here. Check that it has access to the organization, then connect again.";

    public const string ClearSearch = "Clear search";
    public const string ClearFilters = "Clear filters";
    public const string ClearFiltersName = "Clear search and filters";
    public const string ProjectLabel = "_Project";
    public const string ProjectName = "Filter by project";
    public const string ProjectAll = "All projects";
    public const string SizeUnknown = "size unknown";
    public const string SizeUnknownName = "size unknown";
    public const string UnknownSizeNote = "Azure DevOps doesn't report file sizes before download. Files over the size limit are skipped during download.";
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
    public const string ChangeToken = "Change token";
    public const string ChangeBranch = "Change branch";
    public const string TryAgain = "Try again";
    public const string AuthMessage = "It may have expired or been revoked, or it can't read this organization. Check the token and connect again.";
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

    public const string GitHubTokenPageUrl = "https://github.com/settings/personal-access-tokens";

    public const string AzureDevOpsTokenPageUrl =
        "https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate";

    public const string StatusNotConnected = "Not connected";
    public const string StatusConnecting = "Connecting…";
    public const string Disconnect = "Disconnect";
    public const string DisconnectName = "Disconnect and forget the access token";
    public const string StepConnect = "Connect";
    public const string StepRepository = "Repository";
    public const string StepBranch = "Branch";
    public const string StepFiles = "Files";
    public const string StepProceed = "Proceed";
    public const string ConnectionTitle = "Connection";
    public const string ConnectingMessage = "Checking your token and finding every repository it can see.";
    public const string ConnectAgain = "Connect again";
    public const string PromptOverline = "What's next";
    public const string StepDoneState = "Done";
    public const string StepCurrentState = "Current step";
    public const string StepUpcomingState = "Not available yet";
    public const string ProjectPrefix = "Project ";
    public const string NoDescription = "No description";
    public const string BranchesName = "Branches";
    public const string ShowPassphraseName = "Show passphrase";
    public const string HidePassphraseName = "Hide passphrase";
    public const string SshServerSectionHint = "Where the server lives on the network.";
    public const string SshAuthSectionHint = "Sign in with an SSH key. Passwords are not supported.";
    public const string SshFolderSectionHint = "The folder to fetch from. Everything under it is read.";
    public const string StepperName = "Fetch progress steps";

    public const string OrganizationSectionTitle = "Organization";
    public const string TokenSectionTitle = "Access token";
    public const string OrganizationUrlLabel = "Organization _URL";
    public const string OrganizationUrlName = "Organization URL";
    public const string GitHubOrganizationHint = "The GitHub organization whose repositories you want to fetch.";
    public const string AzureDevOpsOrganizationHint = "The Azure DevOps organization whose repositories you want to fetch.";
    public const string TokenLabel = "Personal access _token";
    public const string TokenName = "Personal access token";

    public const string GitHubTokenHint =
        "A personal access token that can read the organization's repositories. It is kept in memory for this session only.";

    public const string AzureDevOpsTokenHint =
        "A personal access token with Code (Read). It is kept in memory for this session only.";

    public const string GitHubTokenPlaceholder = "github_pat_…";
    public const string AzureDevOpsTokenPlaceholder = "Paste your Azure DevOps PAT";
    public const string ShowToken = "Show";
    public const string HideToken = "Hide";
    public const string ShowTokenName = "Show the access token";
    public const string HideTokenName = "Hide the access token";
    public const string GitHubTokenLink = "Create a token on GitHub";
    public const string AzureDevOpsTokenLink = "Create a token in Azure DevOps";
    public const string ConnectAndFind = "Connect and find _repositories";
    public const string ConnectAndFindName = "Connect and find repositories";

    public const string VisibilityLabel = "Visibility";
    public const string VisibilityAll = "All";
    public const string VisibilityPublic = "Public";
    public const string VisibilityPrivate = "Private";
    public const string SortLabel = "Sort by";
    public const string SortName = "Name";
    public const string SortRecentlyUpdated = "Recently updated";
    public const string PublicTag = "Public";
    public const string SearchBranchesLabel = "Search branches";
    public const string SearchBranchesPlaceholder = "Filter by branch name";
    public const string DefaultBadge = "Default";
    public const string ProtectedBadge = "Protected";
    public const string NoBranchMatch = "No branches match your search.";

    public const string SshServerSectionTitle = "Server";
    public const string SshAuthSectionTitle = "Authentication";
    public const string SshFolderSectionTitle = "Folder";
    public const string SshPassphraseLabel = "_Passphrase (optional)";
    public const string SshPassphraseName = "Key passphrase";
    public const string SshPassphraseHint = "Only needed when the key is protected. Kept in memory for this session only.";
    public const string SshFolderLabel = "Start _folder";
    public const string SshFolderName = "Start folder";
    public const string SshFolderPlaceholder = "/var/data or ~";
    public const string SshFolderHint = "An absolute path, or ~ for the home folder.";

    public const string ConnectPromptText =
        "Enter your organization URL and a personal access token, then select Connect and find repositories.";

    public const string FilesPromptText =
        "Check the files you want to process, then select Proceed. Nothing is downloaded until then.";

    public const string ProceedPromptText = "Fetching and splitting your selected files. You can keep working.";

    public const string ProceedDonePromptText =
        "Done. Review the documents below, or change the files and fetch again.";

    public const string ChooseFiles = "Choose files";
    public const string FileSearchName = "Search files by path";
    public const string FileSearchPlaceholder = "Search files by path";
    public const string SelectAll = "Select all";
    public const string SelectAllMatching = "Select all matching";
    public const string ClearSelection = "Clear";
    public const string ClearMatching = "Clear matching";
    public const string SelectAllName = "Select all files";
    public const string SelectAllMatchingName = "Select all matching files";
    public const string ClearSelectionName = "Clear selected files";
    public const string ClearMatchingName = "Clear matching files";
    public const string OnlySupported = "Only supported files";
    public const string OnlySupportedName = "Keep only supported files";
    public const string FileTreeName = "Repository files";
    public const string EmptyTree = "This branch has no files the collector can read.";
    public const string NoFilesSelected = "No files selected";
    public const string SelectFileHint = "Select at least one supported file to continue.";
    public const string ProceedLabel = "Proceed";
    public const string ProceedName = "Proceed, fetch and split selected files";
    public const string ChangeFiles = "Change files";
    public const string ChangeFilesName = "Change files and choose again";
    public const string UnsupportedLabel = "Unsupported";
    public const string TooLargeLabel = "Too large";
    public const string TruncatedTreeNotice =
        "The provider returned only part of the file list because the repository is very large. Some files are not shown.";

    private const int StepCount = 5;

    public static string StepPrompt(int step, string text) => $"Step {step} of {StepCount} - {text}";

    public static string ConnectedTo(string organization) => $"Connected to {organization}";

    public static string RepositoryPromptText(int count, string organization) =>
        $"We found {Plural(count, "repository", "repositories")} in {organization}. Pick the one you want to fetch.";

    public static string BranchPromptText(string repository, int count) =>
        $"{repository} has {Plural(count, "branch", "branches")}. Pick the branch whose files you want to fetch. The default branch is listed first.";

    public static string BranchAutomationName(string name, bool isDefault, bool isProtected, string? shortSha) =>
        string.Join(
            ", ",
            new[] { name, isDefault ? "default" : null, isProtected ? "protected" : null, shortSha is null ? null : Commit(shortSha) }
                .OfType<string>());

    public static string UpdatedText(string relative) => $"Updated {relative}";

    public static string ProjectUpdatedText(string relative) => $"Updated {relative} (project)";

    public static string NoMatchInProject(string project) => $"No repositories in {project}.";

    public static string NoMatchInProjectQuery(string query, string project) => $"No repositories in {project} match \"{query}\".";

    public static string NoMatchFiltered() => "No repositories match these filters.";

    public static string SizeAtLeast(string size, int unknown) =>
        $"at least {size} ({unknown:N0} {(unknown == 1 ? "size" : "sizes")} unknown)";

    public static string FilesSummaryUnknownSize(int files, int unsupported) =>
        $"{files:N0} files · {SizeUnknown} · {unsupported:N0} unsupported skipped";

    public static string SkippedUnsupportedOnly(int unsupported) => $"{unsupported:N0} unsupported skipped";

    private static string Plural(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";


    public static string OrganizationNotFound(string organization) =>
        $"Couldn't find the organization \"{organization}\". Check the spelling, or check that your token was created for it.";

    public static string EmptyAzureDevOps(string organization) =>
        $"This token can't see any repositories in {organization}. Check that it has Code (Read) for this organization, then reload.";

    public static string Showing(int visible, int total) => $"Showing {visible} of {total}";

    public static string NoMatch(string query) => $"No repositories match \"{query}\".";

    public static string OverLimitTag(string limit) => $"Over {limit}";

    public static string LoadingTree(string branch) => $"Reading the file tree for {branch}…";

    public static string NoFileMatch(string query) => $"No files match \"{query}\".";

    public static string FilesSummary(int files, string size, int unsupported, int tooLarge) =>
        $"{files:N0} files · {size} · {unsupported:N0} unsupported · {tooLarge:N0} too large skipped";

    public static string SelectionText(int files, string size) => $"{files:N0} files · {size}";

    public static string SkippedText(int unsupported, int tooLarge) =>
        $"{unsupported:N0} unsupported · {tooLarge:N0} too large skipped";

    public static string OverLimitHint(int limit) =>
        $"That is more than {limit:N0} files. Narrow the selection to continue.";

    public static string HiddenExcludedNotice(int count) =>
        $"{count:N0} files in build and dependency folders (node_modules, bin, obj, .git) are hidden.";

    public static string FolderSize(int selected, int total) => $"{selected:N0} of {total:N0} files";

    public static string FolderRowName(string name, int level, bool expanded, string check, string size) =>
        $"{name}, folder, level {level}, {(expanded ? "expanded" : "collapsed")}, {check}, {size} selected";

    public static string FileRowName(string name, string size, int level, bool selected, string verdict) =>
        $"{name}, {size}, level {level}, {(selected ? "selected" : "not selected")}{verdict}";

    public static string CheckStateName(bool? isChecked) => isChecked switch
    {
        true => "selected",
        false => "not selected",
        null => "partly selected",
    };

    public static string FileVerdictSuffix(bool unsupported, bool tooLarge) =>
        unsupported ? ", unsupported" : tooLarge ? ", too large, will be skipped" : string.Empty;

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

    public static string MissingTitle(string provider) => $"Enter your {provider} token.";

    public static string MissingMessage(string provider) =>
        $"Listing and fetching {provider} repositories uses your own personal access token.";

    public static string ChangeTokenName(string provider) => $"Go back to enter your {provider} token";

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

    public const string SshHostLabel = "_IP address or hostname";
    public const string SshHostName = "IP address or hostname";
    public const string SshHostPlaceholder = "example.com or 192.0.2.1";

    public const string SshPortLabel = "_Port";
    public const string SshPortName = "Port";
    public const string SshPortPlaceholder = "22";

    public const string SshUsernameLabel = "SSH _username";
    public const string SshUsernameName = "SSH username";
    public const string SshUsernamePlaceholder = "Username";

    public const string SshKeyFileLabel = "Private _key file";
    public const string SshKeyFileName = "Private key file";
    public const string SshKeyFilePlaceholder = "Choose a private key file";
    public const string SshBrowseKeyFile = "_Browse…";
    public const string SshBrowseKeyFileName = "Browse for a private key file";
    public const string SshKeyFileDialogTitle = "Choose a private key file";
    public const string SshKeyFileDialogFilter = "All files (*.*)|*.*";

    public const string SshConnect = "Connect and _browse files";
    public const string SshConnectName = "Connect and browse files";
    public const string SshConnectHelp = "Fill in the server, username, key file, and folder first.";

    public const string SshConnecting = "Connecting…";

    public const string SshKeyFileNotFound = "The key file couldn't be found. Pick it again.";

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

    public static string AzureRepositoryAutomationName(AzureRowDescription row) =>
        $"{row.Name}, project {row.Project}, default branch {row.Branch}, {row.Size}"
        + (row.Updated is null ? string.Empty : $", project updated {row.Updated}")
        + (row.OverLimit is null ? string.Empty : $", over the {row.OverLimit} fetch limit");

    public static string CortexaRepositoryAutomationName(CortexaRowDescription row) =>
        $"{row.FullName} @ {row.Branch}{(row.Upstream is null ? string.Empty : $", from {row.Upstream}")}, {row.Size}"
        + (row.OverLimit is null ? string.Empty : $", over the {row.OverLimit} fetch limit");
}

public sealed record CortexaRowDescription(string FullName, string Branch, string? Upstream, string Size, string? OverLimit);

public sealed record AzureRowDescription(string Name, string Project, string Branch, string Size, string? Updated, string? OverLimit);
