using System.Security.Claims;
using Cortexa.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Cortexa.Identity.Infrastructure.Services;

public sealed class HttpContextCurrentActor : ICurrentActor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentActor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var raw = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var userId) ? userId : null;
        }
    }

    public string? Role => _httpContextAccessor.HttpContext?.User.FindFirst("role")?.Value;
}
