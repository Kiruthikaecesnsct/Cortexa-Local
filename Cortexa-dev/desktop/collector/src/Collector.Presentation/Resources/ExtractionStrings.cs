namespace Collector.Presentation.Resources;

public static class ExtractionStrings
{
    public const string ScreenTitle = "Extract";
    public const string PickFilesDialogTitle = "Choose files to parse";
    public const string PickFiles = "Choose files…";
    public const string PickFilesName = "Choose files";
    public const string ClearAll = "Clear all";
    public const string ClearAllName = "Clear all documents";
    public const string RemoveName = "Remove";

    public const string EmptyTitle = "Nothing parsed yet";

    public const string EmptyBody =
        "Choose PDFs, DOCX files, text, or source code. Cortexa splits each file into extraction units with token counts.";

    public const string PreviewEmptyTitle = "No units to preview";
    public const string PreviewEmptyBody = "This document hasn't been extracted. Pick a document that finished extracting.";

    public const string DocumentsTitle = "Documents";
    public const string UnitPreviewTitle = "Unit preview";

    public const string StatFiles = "Files";
    public const string StatExtracted = "Extracted";
    public const string StatUnits = "Units";
    public const string StatTokens = "Tokens";
    public const string StatSkipped = "Skipped";

    public const string StatusPending = "Pending";
    public const string StatusExtracting = "Extracting";
    public const string StatusExtracted = "Extracted";
    public const string StatusFailed = "Failed";
    public const string StatusExcluded = "Excluded";

    public const string KindPage = "Page";
    public const string KindSection = "Section";
    public const string KindFile = "File";
    public const string KindModule = "Module";

    public const string TokensSuffix = "tokens";
    public const string WindowSuffix = " (window)";

    public const string EstimateTitle = "Estimated tokens";
    public const string EstimatePrompt = "Prompt";
    public const string EstimateOutput = "Expected output";
    public const string EstimateTotal = "Total";

    public const string EstimateApproximateNote =
        "Approximate. Counted with an OpenAI tokenizer (Cl100kBase) as a cross-provider stand-in; the selected provider may count tokens differently.";

    public const string ProviderModelTitle = "Provider and model";
    public const string ProviderLabel = "_Provider";
    public const string ProviderName = "Provider";
    public const string ModelLabel = "_Model";
    public const string ModelName = "Model";

    public const string SkipTooLarge = "File is too large to parse.";
    public const string SkipBinaryContent = "File content could not be read as text.";
    public const string SkipUnsupportedFormat = "This file type isn't supported.";
    public const string SkipParseFailure = "Parsing failed.";

    public const string AllSkippedTitle = "No files could be parsed.";
    public const string AllSkippedMessage = "Every file picked was excluded or failed. Remove them and try different files.";
    public const string PartialSkipTitle = "Some files were skipped.";
    public const string PartialSkipMessage = "Review the skipped files below.";

    public const string ExtractKnowledge = "Extract knowledge";
    public const string ExtractKnowledgeName = "Extract knowledge from parsed documents";
    public const string ExtractDisabledHelp = "Parse at least one document first.";
    public const string RunTitle = "Extracting knowledge…";
    public const string RunProgressName = "Knowledge extraction progress";
    public const string Cancel = "Cancel";
    public const string CancelName = "Cancel knowledge extraction";
    public const string CanceledTitle = "Knowledge extraction canceled.";
    public const string CanceledMessage = "Nothing was kept. Run it again when you're ready.";
    public static string KeyMissingTitle(string provider) => $"Add your {provider} key in Settings.";

    public static string KeyMissingMessage(string provider) => $"Knowledge extraction uses your own {provider} API key.";
    public const string OpenSettings = "Open Settings";
    public const string RunFailedTitle = "Knowledge extraction failed.";
    public const string RunFailedMessage = "No units could be processed. Check your connection and key, then try again.";
    public const string ReadySignInTitle = "Knowledge is ready to review.";
    public const string ReadySignInMessage = "Sign in to review and upload it.";
    public const string SignIn = "Sign in";

    public static string RunProgress(int done, int total) => $"{done} of {total} units";

    public static string ParsingProgress(int current, int total) => $"Parsing {current} of {total} files…";

    public static string ReadyStatus(int files) => files == 1 ? "1 file parsed." : $"{files} files parsed.";

    public static string DocumentAutomationName(string filename, string status, int units) =>
        $"{filename}, {status}, {units} units";

    public static string SkipAutomationName(string filename, string reason) => $"{filename}, excluded, {reason}";

    public static string UnitPreviewHeader(string filename, int units, int tokens) =>
        $"{filename} · {units} units · {tokens} tokens";
}
