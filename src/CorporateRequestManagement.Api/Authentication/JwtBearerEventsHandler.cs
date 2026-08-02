using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Api.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CorporateRequestManagement.Api.Authentication;

internal sealed class JwtBearerEventsHandler(IUserRepository users)
{
    public async Task TokenValidated(TokenValidatedContext context)
    {
        string? subject = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        string? role = context.Principal?.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(subject, out Guid id)) { context.Fail("Invalid identity."); return; }
        var user = await users.GetByIdAsync(id, context.HttpContext.RequestAborted);
        if (user is null || user.IsDeleted || !user.IsActive || !string.Equals(role, user.Role.ToString(), StringComparison.Ordinal))
            context.Fail("Invalid identity.");
    }
    public static Task Challenge(JwtBearerChallengeContext context)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        context.HandleResponse();
        return ProblemDetailsResponse.WriteAsync(context.HttpContext, 401, "auth.unauthorized", "Yetkisiz", "Kimlik doğrulaması gerekli.");
    }
    public static Task Forbidden(ForbiddenContext context) => context.Response.HasStarted ? Task.CompletedTask :
        ProblemDetailsResponse.WriteAsync(context.HttpContext, 403, "auth.forbidden", "Erişim reddedildi", "Bu işlem için yetkiniz yok.");
}
