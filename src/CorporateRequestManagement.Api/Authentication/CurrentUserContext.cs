using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CorporateRequestManagement.Application.Abstractions.Identity;

namespace CorporateRequestManagement.Api.Authentication;

internal sealed class CurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true && UserId is not null;
    public Guid? UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out Guid id) ? id : null;
}
