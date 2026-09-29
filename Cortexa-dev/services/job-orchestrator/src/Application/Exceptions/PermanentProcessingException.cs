namespace Cortexa.JobOrchestrator.Application.Exceptions;

public sealed class PermanentProcessingException : Exception
{
    public PermanentProcessingException(string message) : base(message) { }
}
