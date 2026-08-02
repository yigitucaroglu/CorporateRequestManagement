namespace CorporateRequestManagement.Application.Abstractions.Identity;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }
}
