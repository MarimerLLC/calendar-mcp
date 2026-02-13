using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Custom AuthenticationStateProvider that reads the admin auth cookie
/// to provide authentication state for Blazor Server components.
/// </summary>
public class AdminAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AdminAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(user));
    }
}
