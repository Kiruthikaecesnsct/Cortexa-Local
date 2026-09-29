using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Interfaces;

public sealed record EntraAssignment(Role Role, Guid OrganizationId);

public interface IEntraGroupMapper
{
    EntraAssignment Map(IReadOnlyList<string> groupIds);
}
