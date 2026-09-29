namespace Cortexa.ModelRouter.Application.Exceptions;

public sealed class FoundryCapacityExceededException : Exception
{
    public string Deployment { get; }
    public int MaxInFlight { get; }

    public FoundryCapacityExceededException(string deployment, int maxInFlight)
        : base($"Foundry deployment '{deployment}' is at its concurrency limit ({maxInFlight} in-flight requests)")
    {
        Deployment = deployment;
        MaxInFlight = maxInFlight;
    }
}
