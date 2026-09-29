namespace Cortexa.Identity.Domain.Exceptions;

public sealed class ImmutableUserException : Exception
{
    public ImmutableUserException() : base("This user is a system-managed account and cannot be modified.") { }
}
