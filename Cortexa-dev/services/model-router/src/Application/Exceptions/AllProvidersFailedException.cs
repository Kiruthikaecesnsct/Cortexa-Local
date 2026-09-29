namespace Cortexa.ModelRouter.Application.Exceptions;

public sealed class AllProvidersFailedException : Exception
{
    public string PrimaryProvider { get; }
    public string SecondaryProvider { get; }
    public Exception PrimaryError { get; }
    public Exception SecondaryError { get; }

    public AllProvidersFailedException(
        string primaryProvider,
        Exception primaryError,
        string secondaryProvider,
        Exception secondaryError)
        : base(BuildMessage(primaryProvider, primaryError, secondaryProvider, secondaryError))
    {
        PrimaryProvider = primaryProvider;
        PrimaryError = primaryError;
        SecondaryProvider = secondaryProvider;
        SecondaryError = secondaryError;
    }

    private static string BuildMessage(
        string primaryProvider,
        Exception primaryError,
        string secondaryProvider,
        Exception secondaryError) =>
        $"All providers failed: {primaryProvider} ({primaryError.Message}); {secondaryProvider} ({secondaryError.Message})";
}
