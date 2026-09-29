namespace Cortexa.ModelRouter.Application.Exceptions;

public sealed class ModelProviderException : Exception
{
    public string ProviderName { get; }
    public int? HttpStatusCode { get; }
    public string? ErrorCode { get; }
    public string[]? ContentFilterCategories { get; }

    public ModelProviderException(string providerName, int? httpStatusCode, string message)
        : base(message)
    {
        ProviderName = providerName;
        HttpStatusCode = httpStatusCode;
    }

    public ModelProviderException(
        string providerName,
        int? httpStatusCode,
        string message,
        string? errorCode,
        string[]? contentFilterCategories)
        : base(message)
    {
        ProviderName = providerName;
        HttpStatusCode = httpStatusCode;
        ErrorCode = errorCode;
        ContentFilterCategories = contentFilterCategories;
    }
}
