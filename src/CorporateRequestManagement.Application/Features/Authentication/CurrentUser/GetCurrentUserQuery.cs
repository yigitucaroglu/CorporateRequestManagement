using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.Authentication.Common;
using CorporateRequestManagement.Domain.Entities;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Authentication.CurrentUser;

public sealed record GetCurrentUserQuery : IRequest<AuthenticatedUserDto>;
internal sealed class GetCurrentUserQueryHandler(ICurrentUserContext current, IUserRepository users) : IRequestHandler<GetCurrentUserQuery, AuthenticatedUserDto>
{
    public async Task<AuthenticatedUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!current.IsAuthenticated || current.UserId is null) throw Unauthorized();
        User? user = await users.GetByIdAsync(current.UserId.Value, cancellationToken);
        if (user is null || user.IsDeleted || !user.IsActive) throw Unauthorized();
        return new(user.Id, user.FirstName, user.LastName, user.Email, user.Role.ToString(), user.DepartmentId);
    }
    private static AuthenticationFailedException Unauthorized() => new("auth.unauthorized", "Kimlik doğrulaması gerekli.");
}
