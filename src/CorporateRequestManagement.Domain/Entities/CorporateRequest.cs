using CorporateRequestManagement.Domain.Common;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.Domain.Entities;

public sealed class CorporateRequest : SoftDeletableEntity
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int RejectionDescriptionMaxLength = 1000;

    private readonly List<RequestApproval> _approvals = [];
    private byte[] _rowVersion = [];

    private CorporateRequest()
    {
    }

    private CorporateRequest(
        Guid id,
        string title,
        string description,
        RequestType requestType,
        decimal? amount,
        Guid createdByUserId,
        Guid departmentId,
        DateTimeOffset createdDate)
        : base(id)
    {
        ValidateOwner(createdByUserId);
        ValidateDepartment(departmentId);
        ValidateCreationDate(createdDate);
        string validatedTitle = RequiredText(title, TitleMaxLength, "request.title_required", "request.title_too_long");
        string validatedDescription = RequiredText(
            description,
            DescriptionMaxLength,
            "request.description_required",
            "request.description_too_long");
        ValidateRequestTypeAndAmount(requestType, amount);

        Title = validatedTitle;
        Description = validatedDescription;
        RequestType = requestType;
        Amount = amount;
        CreatedByUserId = createdByUserId;
        DepartmentId = departmentId;
        Status = RequestStatus.Draft;
        CreatedDate = createdDate;
        UpdatedDate = createdDate;
    }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public RequestType RequestType { get; private set; }

    public decimal? Amount { get; private set; }

    public RequestStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public DateTimeOffset UpdatedDate { get; private set; }

    public DateTimeOffset? SubmittedDate { get; private set; }

    public DateTimeOffset? CompletedDate { get; private set; }

    public byte[] RowVersion => _rowVersion.ToArray();

    public IReadOnlyCollection<RequestApproval> Approvals => _approvals.AsReadOnly();

    public static CorporateRequest Create(
        Guid id,
        string title,
        string description,
        RequestType requestType,
        decimal? amount,
        Guid createdByUserId,
        Guid departmentId,
        DateTimeOffset createdDate)
    {
        return new CorporateRequest(id, title, description, requestType, amount, createdByUserId, departmentId, createdDate);
    }

    public void UpdateDraft(
        Guid actorUserId,
        string title,
        string description,
        RequestType requestType,
        decimal? amount,
        DateTimeOffset now)
    {
        ValidateActor(actorUserId);
        EnsureOwner(actorUserId);
        EnsureStatus(RequestStatus.Draft, "request.update_requires_draft", "Yalnız taslak talep güncellenebilir.");
        ValidateTimestamp(now);
        string validatedTitle = RequiredText(title, TitleMaxLength, "request.title_required", "request.title_too_long");
        string validatedDescription = RequiredText(
            description,
            DescriptionMaxLength,
            "request.description_required",
            "request.description_too_long");
        ValidateRequestTypeAndAmount(requestType, amount);

        Title = validatedTitle;
        Description = validatedDescription;
        RequestType = requestType;
        Amount = amount;
        UpdatedDate = now;
    }

    public void Submit(Guid actorUserId, DateTimeOffset now)
    {
        ValidateActor(actorUserId);
        EnsureOwner(actorUserId);
        EnsureStatus(RequestStatus.Draft, "request.submit_requires_draft", "Yalnız taslak talep gönderilebilir.");
        ValidateTimestamp(now);

        Status = RequestStatus.PendingManagerApproval;
        SubmittedDate = now;
        UpdatedDate = now;
    }

    public void ApproveByManager(Guid approvalId, Guid managerUserId, decimal approvalThreshold, DateTimeOffset now)
    {
        EnsureStatus(
            RequestStatus.PendingManagerApproval,
            "request.manager_approval_invalid_status",
            "Talep yönetici onayı beklemiyor.");
        ValidateActor(managerUserId);
        ValidateApprovalId(approvalId);
        EnsureDifferentFromOwner(managerUserId);
        ValidateTimestamp(now);

        if (approvalThreshold <= 0)
        {
            throw new DomainRuleException("request.approval_threshold_invalid", "Onay eşiği sıfırdan büyük olmalıdır.");
        }

        EnsureApprovalIdIsUnique(approvalId);

        RequestApproval approval = RequestApproval.Create(
            approvalId,
            Id,
            managerUserId,
            ApprovalLevel.Manager,
            ApprovalStatus.Approved,
            null,
            now);
        RequestStatus nextStatus = Amount is null || Amount <= approvalThreshold
            ? RequestStatus.Approved
            : RequestStatus.PendingAdminApproval;
        DateTimeOffset? completedDate = nextStatus == RequestStatus.Approved ? now : null;

        Status = nextStatus;
        CompletedDate = completedDate;
        UpdatedDate = now;
        _approvals.Add(approval);
    }

    public void ApproveByAdmin(Guid approvalId, Guid adminUserId, DateTimeOffset now)
    {
        EnsureStatus(
            RequestStatus.PendingAdminApproval,
            "request.admin_approval_invalid_status",
            "Talep yönetim onayı beklemiyor.");
        ValidateActor(adminUserId);
        ValidateApprovalId(approvalId);
        ValidateTimestamp(now);
        EnsureApprovalIdIsUnique(approvalId);

        RequestApproval approval = RequestApproval.Create(
            approvalId,
            Id,
            adminUserId,
            ApprovalLevel.Admin,
            ApprovalStatus.Approved,
            null,
            now);

        Status = RequestStatus.Approved;
        CompletedDate = now;
        UpdatedDate = now;
        _approvals.Add(approval);
    }

    public void RejectByManager(
        Guid approvalId,
        Guid managerUserId,
        string description,
        DateTimeOffset now)
    {
        EnsureStatus(
            RequestStatus.PendingManagerApproval,
            "request.manager_rejection_invalid_status",
            "Talep yönetici onayı beklemiyor.");
        ValidateActor(managerUserId);
        ValidateApprovalId(approvalId);
        EnsureDifferentFromOwner(managerUserId);
        ValidateTimestamp(now);
        ValidateRejectionDescription(description);
        EnsureApprovalIdIsUnique(approvalId);
        Reject(approvalId, managerUserId, ApprovalLevel.Manager, description, now);
    }

    public void RejectByAdmin(Guid approvalId, Guid adminUserId, string description, DateTimeOffset now)
    {
        EnsureStatus(
            RequestStatus.PendingAdminApproval,
            "request.admin_rejection_invalid_status",
            "Talep yönetim onayı beklemiyor.");
        ValidateActor(adminUserId);
        ValidateApprovalId(approvalId);
        ValidateTimestamp(now);
        ValidateRejectionDescription(description);
        EnsureApprovalIdIsUnique(approvalId);
        Reject(approvalId, adminUserId, ApprovalLevel.Admin, description, now);
    }

    public void CancelByOwner(Guid actorUserId, DateTimeOffset now)
    {
        ValidateActor(actorUserId);
        EnsureOwner(actorUserId);
        if (Status is not RequestStatus.Draft and not RequestStatus.PendingManagerApproval)
        {
            throw new DomainRuleException("request.cancellation_invalid_status", "Talep bu durumda iptal edilemez.");
        }

        ValidateTimestamp(now);

        Status = RequestStatus.Cancelled;
        CompletedDate = now;
        UpdatedDate = now;
    }

    public void SoftDelete(DateTimeOffset deletedDate, Guid deletedByUserId)
    {
        ValidateActor(deletedByUserId);
        ValidateTimestamp(deletedDate);
        MarkDeleted(deletedDate, deletedByUserId);
        UpdatedDate = deletedDate;
    }

    private void Reject(
        Guid approvalId,
        Guid actorUserId,
        ApprovalLevel approvalLevel,
        string description,
        DateTimeOffset now)
    {
        RequestApproval approval = RequestApproval.Create(
            approvalId,
            Id,
            actorUserId,
            approvalLevel,
            ApprovalStatus.Rejected,
            description,
            now);

        Status = RequestStatus.Rejected;
        CompletedDate = now;
        UpdatedDate = now;
        _approvals.Add(approval);
    }

    private static void ValidateRequestTypeAndAmount(RequestType requestType, decimal? amount)
    {
        if (!Enum.IsDefined(requestType))
        {
            throw new DomainRuleException("request.type_invalid", "Talep tipi geçersiz.");
        }

        if (requestType is RequestType.Expense or RequestType.Equipment && amount is null)
        {
            throw new DomainRuleException("request.amount_required", "Bu talep tipi için tutar zorunludur.");
        }

        if (amount is <= 0)
        {
            throw new DomainRuleException("request.amount_must_be_positive", "Tutar sıfırdan büyük olmalıdır.");
        }

    }

    private static void ValidateRejectionDescription(string description)
    {
        RequiredText(
            description,
            RejectionDescriptionMaxLength,
            "request.rejection_description_required",
            "request.rejection_description_too_long");
    }

    private void ValidateTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp < UpdatedDate)
        {
            throw new DomainRuleException("request.timestamp_out_of_order", "İşlem zamanı son güncelleme zamanından önce olamaz.");
        }
    }

    private void EnsureApprovalIdIsUnique(Guid approvalId)
    {
        if (_approvals.Any(approval => approval.Id == approvalId))
        {
            throw new DomainRuleException("request.approval_id_duplicate", "Onay kimliği daha önce kullanılmış.");
        }
    }

    private void EnsureOwner(Guid actorUserId)
    {
        if (actorUserId != CreatedByUserId)
        {
            throw new DomainRuleException("request.owner_required", "Bu işlem yalnız talep sahibi tarafından yapılabilir.");
        }
    }

    private void EnsureDifferentFromOwner(Guid actorUserId)
    {
        if (actorUserId == CreatedByUserId)
        {
            throw new DomainRuleException("request.self_approval_forbidden", "Kullanıcı kendi talebini onaylayamaz veya reddedemez.");
        }
    }

    private void EnsureStatus(RequestStatus expectedStatus, string code, string message)
    {
        if (Status != expectedStatus)
        {
            throw new DomainRuleException(code, message);
        }
    }

    private static void ValidateOwner(Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new DomainRuleException("request.owner_required", "Talep sahibi zorunludur.");
        }
    }

    private static void ValidateActor(Guid actorId)
    {
        if (actorId == Guid.Empty)
        {
            throw new DomainRuleException("request.actor_required", "İşlem aktörü kimliği boş olamaz.");
        }
    }

    private static void ValidateApprovalId(Guid approvalId)
    {
        if (approvalId == Guid.Empty)
        {
            throw new DomainRuleException("request.approval_id_required", "Onay kimliği boş olamaz.");
        }
    }

    private static void ValidateDepartment(Guid departmentId)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainRuleException("request.department_required", "Departman zorunludur.");
        }
    }

    private static void ValidateCreationDate(DateTimeOffset createdDate)
    {
        if (createdDate == default)
        {
            throw new DomainRuleException("request.created_date_required", "Oluşturulma zamanı zorunludur.");
        }
    }

    private static string RequiredText(string value, int maxLength, string requiredCode, string lengthCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException(requiredCode, "Zorunlu alan boş olamaz.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainRuleException(lengthCode, "Alan izin verilen uzunluğu aşıyor.");
        }

        return trimmed;
    }
}
