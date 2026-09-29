namespace Cortexa.ModelRouter.Application.Exceptions;

public sealed class FoundryTimeoutException : Exception
{
    public string Deployment { get; }
    public int TimeoutSeconds { get; }

    public FoundryTimeoutException(string deployment, int timeoutSeconds)
        : base($"Foundry call to '{deployment}' exceeded server timeout of {timeoutSeconds}s")
    {
        Deployment = deployment;
        TimeoutSeconds = timeoutSeconds;
    }
}
