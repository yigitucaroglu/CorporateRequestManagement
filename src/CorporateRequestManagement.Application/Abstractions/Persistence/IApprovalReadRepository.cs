using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Approvals.Common;
using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.Application.Abstractions.Persistence;

public interface IApprovalReadRepository
{
    Task<PagedResult<ApprovalQueueItemDto>> GetManagerQueueAsync(Guid managerUserId, ApprovalQueueCriteria criteria, CancellationToken cancellationToken = default);
    Task<PagedResult<ApprovalQueueItemDto>> GetAdminQueueAsync(Guid adminUserId, ApprovalQueueCriteria criteria, CancellationToken cancellationToken = default);
    Task<ApprovalRequestDetailDto?> GetManagerDetailAsync(Guid requestId, Guid managerUserId, CancellationToken cancellationToken = default);
    Task<ApprovalRequestDetailDto?> GetAdminDetailAsync(Guid requestId, Guid adminUserId, CancellationToken cancellationToken = default);
    Task<ApprovalRequestDetailDto?> GetDecisionResultAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalHistoryItemDto>?> GetHistoryAsync(Guid requestId, Guid userId, UserRole role, CancellationToken cancellationToken = default);
}
