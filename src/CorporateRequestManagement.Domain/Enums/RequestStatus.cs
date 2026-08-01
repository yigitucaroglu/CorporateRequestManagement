namespace CorporateRequestManagement.Domain.Enums;

public enum RequestStatus
{
    Draft = 1,
    PendingManagerApproval = 2,
    PendingAdminApproval = 3,
    Approved = 4,
    Rejected = 5,
    Cancelled = 6
}
