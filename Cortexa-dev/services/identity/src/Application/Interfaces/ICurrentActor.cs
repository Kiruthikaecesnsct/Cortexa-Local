namespace Cortexa.Identity.Application.Interfaces;

public interface ICurrentActor
{
    Guid? UserId { get; }
    string? Role { get; }
}
