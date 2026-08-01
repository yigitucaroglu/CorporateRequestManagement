using CorporateRequestManagement.Domain.Common;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.Domain.Entities;

public sealed class RequestApproval : Entity
{
    private RequestApproval()
    {
    }

    private RequestApproval(
        Guid id,
        Guid requestId,
        Guid approvedByUserId,
        ApprovalLevel approvalLevel,
        ApprovalStatus status,
        string? description,
        DateTimeOffset actionDate)
        : base(id)
    {
        if (requestId == Guid.Empty)
        {
            throw new DomainRuleException("request.approval_request_required", "Talep kimliği boş olamaz.");
        }

        if (approvedByUserId == Guid.Empty)
        {
            throw new DomainRuleException("request.approval_actor_required", "Onay aktörü kimliği boş olamaz.");
        }

        if (!Enum.IsDefined(approvalLevel))
        {
            throw new DomainRuleException("request.approval_level_invalid", "Onay seviyesi geçersiz.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new DomainRuleException("request.approval_status_invalid", "Onay sonucu geçersiz.");
        }

        if (actionDate == default)
        {
            throw new DomainRuleException("request.approval_date_required", "Onay zamanı zorunludur.");
        }

        if (status == ApprovalStatus.Rejected && string.IsNullOrWhiteSpace(description))
        {
            throw new DomainRuleException("request.rejection_description_required", "Ret açıklaması zorunludur.");
        }

        string? trimmedDescription = description?.Trim();
        if (trimmedDescription?.Length > CorporateRequest.RejectionDescriptionMaxLength)
        {
            throw new DomainRuleException("request.rejection_description_too_long", "Ret açıklaması izin verilen uzunluğu aşıyor.");
        }

        RequestId = requestId;
        ApprovedByUserId = approvedByUserId;
        ApprovalLevel = approvalLevel;
        Status = status;
        Description = string.IsNullOrEmpty(trimmedDescription) ? null : trimmedDescription;
        ActionDate = actionDate;
    }

    public Guid RequestId { get; private set; }

    public Guid ApprovedByUserId { get; private set; }

    public ApprovalLevel ApprovalLevel { get; private set; }

    public ApprovalStatus Status { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset ActionDate { get; private set; }

    internal static RequestApproval Create(
        Guid id,
        Guid requestId,
        Guid approvedByUserId,
        ApprovalLevel approvalLevel,
        ApprovalStatus status,
        string? description,
        DateTimeOffset actionDate)
    {
        return new RequestApproval(id, requestId, approvedByUserId, approvalLevel, status, description, actionDate);
    }
}
