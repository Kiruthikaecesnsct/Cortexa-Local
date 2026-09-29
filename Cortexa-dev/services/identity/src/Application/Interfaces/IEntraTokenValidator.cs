namespace Cortexa.Identity.Application.Interfaces;

public sealed record EntraClaims(
    string Email,
    string Name,
    string EntraObjectId,
    IReadOnlyList<string> GroupIds
);

public interface IEntraTokenValidator
{
    Task<EntraClaims> ValidateAsync(string idToken, CancellationToken ct);
}
