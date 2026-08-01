using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Domain.Exceptions;
using System.Collections.ObjectModel;
using System.Reflection;

namespace CorporateRequestManagement.UnitTests.Entities;

public sealed class CorporateRequestTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DepartmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ManagerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AdminId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WhenExpenseAmountIsNull_ShouldRejectRequest()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateRequest(RequestType.Expense, null));

        Assert.Equal("request.amount_required", exception.Code);
    }

    [Fact]
    public void Create_WhenEquipmentAmountIsZero_ShouldRejectRequest()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateRequest(RequestType.Equipment, 0));

        Assert.Equal("request.amount_must_be_positive", exception.Code);
    }

    [Fact]
    public void Create_WhenLeaveAmountIsNull_ShouldCreateDraft()
    {
        CorporateRequest request = CreateRequest(RequestType.Leave, null);

        Assert.Equal(RequestStatus.Draft, request.Status);
        Assert.Null(request.Amount);
        Assert.Empty(request.Approvals);
        Assert.Empty(request.RowVersion);
    }

    [Fact]
    public void Create_WhenOtherAmountIsNegative_ShouldRejectRequest()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateRequest(RequestType.Other, -1));

        Assert.Equal("request.amount_must_be_positive", exception.Code);
    }

    [Fact]
    public void UpdateDraft_WhenActorIsOwner_ShouldUpdateEditableValues()
    {
        CorporateRequest request = CreateRequest();
        DateTimeOffset updatedAt = CreatedAt.AddHours(1);

        request.UpdateDraft(OwnerId, "  Yeni başlık  ", "  Yeni açıklama  ", RequestType.Leave, null, updatedAt);

        Assert.Equal("Yeni başlık", request.Title);
        Assert.Equal("Yeni açıklama", request.Description);
        Assert.Equal(RequestType.Leave, request.RequestType);
        Assert.Null(request.Amount);
        Assert.Equal(updatedAt, request.UpdatedDate);
        Assert.Equal(OwnerId, request.CreatedByUserId);
        Assert.Equal(DepartmentId, request.DepartmentId);
        Assert.Equal(RequestStatus.Draft, request.Status);
    }

    [Fact]
    public void UpdateDraft_WhenActorIsNotOwner_ShouldRejectUpdate()
    {
        CorporateRequest request = CreateRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.UpdateDraft(ManagerId, "Yeni", "Yeni", RequestType.Expense, 10, CreatedAt.AddHours(1)));

        Assert.Equal("request.owner_required", exception.Code);
    }

    [Fact]
    public void UpdateDraft_WhenRequestIsSubmitted_ShouldRejectUpdate()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.UpdateDraft(OwnerId, "Yeni", "Yeni", RequestType.Expense, 10, CreatedAt.AddHours(2)));

        Assert.Equal("request.update_requires_draft", exception.Code);
    }

    [Fact]
    public void Submit_WhenActorIsOwner_ShouldMoveToManagerApproval()
    {
        CorporateRequest request = CreateRequest();
        DateTimeOffset submittedAt = CreatedAt.AddHours(1);

        request.Submit(OwnerId, submittedAt);

        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
        Assert.Equal(submittedAt, request.SubmittedDate);
        Assert.Equal(submittedAt, request.UpdatedDate);
    }

    [Fact]
    public void Submit_WhenRequestWasAlreadySubmitted_ShouldRejectSecondSubmission()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.Submit(OwnerId, CreatedAt.AddHours(2)));

        Assert.Equal("request.submit_requires_draft", exception.Code);
    }

    [Fact]
    public void ApproveByManager_WhenAmountIsAtThreshold_ShouldApproveRequest()
    {
        CorporateRequest request = SubmittedRequest(10_000);
        DateTimeOffset approvedAt = CreatedAt.AddHours(2);

        request.ApproveByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), ManagerId, 10_000, approvedAt);

        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.Equal(approvedAt, request.CompletedDate);
    }

    [Fact]
    public void ApproveByManager_WhenAmountExceedsThreshold_ShouldWaitForAdminApproval()
    {
        CorporateRequest request = SubmittedRequest(10_001);

        request.ApproveByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), ManagerId, 10_000, CreatedAt.AddHours(2));

        Assert.Equal(RequestStatus.PendingAdminApproval, request.Status);
        Assert.Null(request.CompletedDate);
    }

    [Fact]
    public void ApproveByManager_WhenAmountIsNull_ShouldApproveRequest()
    {
        CorporateRequest request = SubmittedRequest(null, RequestType.Leave);

        request.ApproveByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), ManagerId, 10_000, CreatedAt.AddHours(2));

        Assert.Equal(RequestStatus.Approved, request.Status);
    }

    [Fact]
    public void ApproveByManager_WhenManagerOwnsRequest_ShouldRejectApproval()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.ApproveByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), OwnerId, 10_000, CreatedAt.AddHours(2)));

        Assert.Equal("request.self_approval_forbidden", exception.Code);
        Assert.Empty(request.Approvals);
    }

    [Fact]
    public void ApproveByManager_WhenValid_ShouldAppendManagerApprovalHistory()
    {
        CorporateRequest request = SubmittedRequest();
        Guid approvalId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        request.ApproveByManager(approvalId, ManagerId, 10_000, CreatedAt.AddHours(2));

        RequestApproval approval = Assert.Single(request.Approvals);
        Assert.Equal(approvalId, approval.Id);
        Assert.Equal(request.Id, approval.RequestId);
        Assert.Equal(ManagerId, approval.ApprovedByUserId);
        Assert.Equal(ApprovalLevel.Manager, approval.ApprovalLevel);
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
    }

    [Fact]
    public void ApproveByAdmin_WhenRequestDoesNotWaitForAdmin_ShouldRejectApproval()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.ApproveByAdmin(Guid.Parse("66666666-6666-6666-6666-666666666666"), AdminId, CreatedAt.AddHours(2)));

        Assert.Equal("request.admin_approval_invalid_status", exception.Code);
    }

    [Fact]
    public void ApproveByAdmin_WhenRequestWaitsForAdmin_ShouldApproveRequest()
    {
        CorporateRequest request = PendingAdminRequest();
        DateTimeOffset approvedAt = CreatedAt.AddHours(3);

        request.ApproveByAdmin(Guid.Parse("66666666-6666-6666-6666-666666666666"), AdminId, approvedAt);

        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.Equal(approvedAt, request.CompletedDate);
        Assert.Equal(2, request.Approvals.Count);
    }

    [Fact]
    public void RejectByManager_WhenDescriptionIsBlank_ShouldRejectAction()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.RejectByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), ManagerId, "  ", CreatedAt.AddHours(2)));

        Assert.Equal("request.rejection_description_required", exception.Code);
        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
    }

    [Fact]
    public void RejectByManager_WhenValid_ShouldCreateTerminalRejectionAndHistory()
    {
        CorporateRequest request = SubmittedRequest();
        DateTimeOffset rejectedAt = CreatedAt.AddHours(2);

        request.RejectByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), ManagerId, "  Bütçe uygun değil  ", rejectedAt);

        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal(rejectedAt, request.CompletedDate);
        RequestApproval approval = Assert.Single(request.Approvals);
        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.Equal("Bütçe uygun değil", approval.Description);
    }

    [Fact]
    public void CancelByOwner_WhenRequestIsDraft_ShouldCancelRequest()
    {
        CorporateRequest request = CreateRequest();

        request.CancelByOwner(OwnerId, CreatedAt.AddHours(1));

        Assert.Equal(RequestStatus.Cancelled, request.Status);
        Assert.NotNull(request.CompletedDate);
    }

    [Fact]
    public void CancelByOwner_WhenRequestWaitsForManager_ShouldCancelRequest()
    {
        CorporateRequest request = SubmittedRequest();

        request.CancelByOwner(OwnerId, CreatedAt.AddHours(2));

        Assert.Equal(RequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void CancelByOwner_WhenRequestIsApproved_ShouldRejectCancellation()
    {
        CorporateRequest request = SubmittedRequest();
        request.ApproveByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), ManagerId, 10_000, CreatedAt.AddHours(2));

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.CancelByOwner(OwnerId, CreatedAt.AddHours(3)));

        Assert.Equal("request.cancellation_invalid_status", exception.Code);
    }

    [Fact]
    public void SoftDelete_WhenRequestIsActive_ShouldSetDeletionMetadata()
    {
        CorporateRequest request = CreateRequest();
        DateTimeOffset deletedAt = CreatedAt.AddDays(1);

        request.SoftDelete(deletedAt, AdminId);

        Assert.True(request.IsDeleted);
        Assert.Equal(deletedAt, request.DeletedDate);
        Assert.Equal(AdminId, request.DeletedByUserId);
        Assert.Equal(deletedAt, request.UpdatedDate);
    }

    [Fact]
    public void SoftDelete_WhenRequestWasAlreadyDeleted_ShouldRejectSecondDeletion()
    {
        CorporateRequest request = CreateRequest();
        request.SoftDelete(CreatedAt.AddDays(1), AdminId);

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.SoftDelete(CreatedAt.AddDays(2), AdminId));

        Assert.Equal("entity.already_deleted", exception.Code);
    }

    [Fact]
    public void UpdateDraft_WhenExpenseAmountIsNull_ShouldPreserveEntireState()
    {
        CorporateRequest request = CreateRequest();
        DateTimeOffset originalUpdatedDate = request.UpdatedDate;

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.UpdateDraft(OwnerId, "Değişen başlık", "Değişen açıklama", RequestType.Expense, null, CreatedAt.AddHours(1)));

        Assert.Equal("request.amount_required", exception.Code);
        Assert.Equal("Test talebi", request.Title);
        Assert.Equal("Test açıklaması", request.Description);
        Assert.Equal(RequestType.Expense, request.RequestType);
        Assert.Equal(100, request.Amount);
        Assert.Equal(originalUpdatedDate, request.UpdatedDate);
        Assert.Equal(RequestStatus.Draft, request.Status);
    }

    [Fact]
    public void UpdateDraft_WhenRequestTypeIsInvalid_ShouldPreserveEditableState()
    {
        CorporateRequest request = CreateRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.UpdateDraft(OwnerId, "Değişen", "Değişen", (RequestType)999, 25, CreatedAt.AddHours(1)));

        Assert.Equal("request.type_invalid", exception.Code);
        Assert.Equal("Test talebi", request.Title);
        Assert.Equal("Test açıklaması", request.Description);
        Assert.Equal(RequestType.Expense, request.RequestType);
        Assert.Equal(100, request.Amount);
        Assert.Equal(CreatedAt, request.UpdatedDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateDraft_WhenAmountIsNotPositive_ShouldPreserveEditableState(decimal invalidAmount)
    {
        CorporateRequest request = CreateRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.UpdateDraft(OwnerId, "Değişen", "Değişen", RequestType.Expense, invalidAmount, CreatedAt.AddHours(1)));

        Assert.Equal("request.amount_must_be_positive", exception.Code);
        Assert.Equal("Test talebi", request.Title);
        Assert.Equal("Test açıklaması", request.Description);
        Assert.Equal(RequestType.Expense, request.RequestType);
        Assert.Equal(100, request.Amount);
        Assert.Equal(CreatedAt, request.UpdatedDate);
    }

    [Fact]
    public void UpdateDraft_WhenTimestampIsBeforeLastUpdate_ShouldPreserveEntireState()
    {
        CorporateRequest request = CreateRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.UpdateDraft(OwnerId, "Değişen", "Değişen", RequestType.Leave, null, CreatedAt.AddTicks(-1)));

        Assert.Equal("request.timestamp_out_of_order", exception.Code);
        Assert.Equal("Test talebi", request.Title);
        Assert.Equal("Test açıklaması", request.Description);
        Assert.Equal(RequestType.Expense, request.RequestType);
        Assert.Equal(100, request.Amount);
        Assert.Equal(CreatedAt, request.UpdatedDate);
        Assert.Equal(RequestStatus.Draft, request.Status);
    }

    [Fact]
    public void ApproveByManager_WhenActorIsEmpty_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.ApproveByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), Guid.Empty, 10_000, CreatedAt.AddHours(2)));

        Assert.Equal("request.actor_required", exception.Code);
        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
        Assert.Equal(CreatedAt.AddHours(1), request.UpdatedDate);
        Assert.Empty(request.Approvals);
        Assert.Null(request.CompletedDate);
    }

    [Fact]
    public void ApproveByManager_WhenApprovalIdIsEmpty_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.ApproveByManager(Guid.Empty, ManagerId, 10_000, CreatedAt.AddHours(2)));

        Assert.Equal("request.approval_id_required", exception.Code);
        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
        Assert.Empty(request.Approvals);
        Assert.Null(request.CompletedDate);
    }

    [Fact]
    public void ApproveByManager_WhenTimestampIsBeforeLastUpdate_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.ApproveByManager(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                ManagerId,
                10_000,
                CreatedAt));

        Assert.Equal("request.timestamp_out_of_order", exception.Code);
        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
        Assert.Empty(request.Approvals);
        Assert.Null(request.CompletedDate);
        Assert.Equal(CreatedAt.AddHours(1), request.UpdatedDate);
    }

    [Fact]
    public void ApproveByAdmin_WhenApprovalIdWasUsedByManager_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = PendingAdminRequest();
        RequestApproval managerApproval = Assert.Single(request.Approvals);

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.ApproveByAdmin(managerApproval.Id, AdminId, CreatedAt.AddHours(3)));

        Assert.Equal("request.approval_id_duplicate", exception.Code);
        Assert.Equal(RequestStatus.PendingAdminApproval, request.Status);
        Assert.Null(request.CompletedDate);
        Assert.Single(request.Approvals);
        Assert.Same(managerApproval, request.Approvals.Single());
        Assert.Equal(CreatedAt.AddHours(2), request.UpdatedDate);
    }

    [Fact]
    public void ApproveByAdmin_WhenActorIsEmpty_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = PendingAdminRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.ApproveByAdmin(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Guid.Empty,
                CreatedAt.AddHours(3)));

        Assert.Equal("request.actor_required", exception.Code);
        Assert.Equal(RequestStatus.PendingAdminApproval, request.Status);
        Assert.Equal(CreatedAt.AddHours(2), request.UpdatedDate);
        Assert.Null(request.CompletedDate);
        Assert.Single(request.Approvals);
    }

    [Fact]
    public void ApproveByAdmin_WhenTimestampIsBeforeLastUpdate_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = PendingAdminRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.ApproveByAdmin(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                AdminId,
                CreatedAt.AddHours(1)));

        Assert.Equal("request.timestamp_out_of_order", exception.Code);
        Assert.Equal(RequestStatus.PendingAdminApproval, request.Status);
        Assert.Equal(CreatedAt.AddHours(2), request.UpdatedDate);
        Assert.Null(request.CompletedDate);
        Assert.Single(request.Approvals);
    }

    [Fact]
    public void ApproveByAdmin_WhenValid_ShouldRecordCompleteAdminApproval()
    {
        CorporateRequest request = PendingAdminRequest();
        Guid approvalId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        DateTimeOffset actionDate = CreatedAt.AddHours(3);

        request.ApproveByAdmin(approvalId, AdminId, actionDate);

        RequestApproval approval = request.Approvals.Last();
        Assert.Equal(approvalId, approval.Id);
        Assert.Equal(request.Id, approval.RequestId);
        Assert.Equal(AdminId, approval.ApprovedByUserId);
        Assert.Equal(ApprovalLevel.Admin, approval.ApprovalLevel);
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Null(approval.Description);
        Assert.Equal(actionDate, approval.ActionDate);
        Assert.Equal(actionDate, request.UpdatedDate);
    }

    [Fact]
    public void RejectByAdmin_WhenValid_ShouldRecordCompleteAdminRejection()
    {
        CorporateRequest request = PendingAdminRequest();
        Guid approvalId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        DateTimeOffset actionDate = CreatedAt.AddHours(3);

        request.RejectByAdmin(approvalId, AdminId, "  Yönetim reddi  ", actionDate);

        RequestApproval approval = request.Approvals.Last();
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal(actionDate, request.CompletedDate);
        Assert.Equal(approvalId, approval.Id);
        Assert.Equal(AdminId, approval.ApprovedByUserId);
        Assert.Equal(ApprovalLevel.Admin, approval.ApprovalLevel);
        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.Equal("Yönetim reddi", approval.Description);
        Assert.Equal(actionDate, approval.ActionDate);
    }

    [Fact]
    public void RejectByManager_WhenManagerOwnsRequest_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.RejectByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), OwnerId, "Ret", CreatedAt.AddHours(2)));

        Assert.Equal("request.self_approval_forbidden", exception.Code);
        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
        Assert.Empty(request.Approvals);
        Assert.Null(request.CompletedDate);
    }

    [Fact]
    public void RejectByManager_WhenActorIsEmpty_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.RejectByManager(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Guid.Empty,
                "Ret",
                CreatedAt.AddHours(2)));

        Assert.Equal("request.actor_required", exception.Code);
        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
        Assert.Equal(CreatedAt.AddHours(1), request.UpdatedDate);
        Assert.Null(request.CompletedDate);
        Assert.Empty(request.Approvals);
    }

    [Fact]
    public void RejectByManager_WhenTimestampIsBeforeLastUpdate_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.RejectByManager(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                ManagerId,
                "Ret",
                CreatedAt));

        Assert.Equal("request.timestamp_out_of_order", exception.Code);
        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
        Assert.Equal(CreatedAt.AddHours(1), request.UpdatedDate);
        Assert.Null(request.CompletedDate);
        Assert.Empty(request.Approvals);
    }

    [Fact]
    public void RejectByAdmin_WhenActorIsEmpty_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = PendingAdminRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.RejectByAdmin(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Guid.Empty,
                "Ret",
                CreatedAt.AddHours(3)));

        Assert.Equal("request.actor_required", exception.Code);
        Assert.Equal(RequestStatus.PendingAdminApproval, request.Status);
        Assert.Equal(CreatedAt.AddHours(2), request.UpdatedDate);
        Assert.Null(request.CompletedDate);
        Assert.Single(request.Approvals);
    }

    [Fact]
    public void RejectByAdmin_WhenTimestampIsBeforeLastUpdate_ShouldPreserveStateAndHistory()
    {
        CorporateRequest request = PendingAdminRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.RejectByAdmin(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                AdminId,
                "Ret",
                CreatedAt.AddHours(1)));

        Assert.Equal("request.timestamp_out_of_order", exception.Code);
        Assert.Equal(RequestStatus.PendingAdminApproval, request.Status);
        Assert.Equal(CreatedAt.AddHours(2), request.UpdatedDate);
        Assert.Null(request.CompletedDate);
        Assert.Single(request.Approvals);
    }

    [Fact]
    public void CancelByOwner_WhenRequestWaitsForAdmin_ShouldRejectCancellation()
    {
        CorporateRequest request = PendingAdminRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.CancelByOwner(OwnerId, CreatedAt.AddHours(3)));

        Assert.Equal("request.cancellation_invalid_status", exception.Code);
        Assert.Equal(RequestStatus.PendingAdminApproval, request.Status);
        Assert.Single(request.Approvals);
    }

    [Fact]
    public void CancelByOwner_WhenRequestIsAlreadyCancelled_ShouldRejectCancellation()
    {
        CorporateRequest request = CreateRequest();
        request.CancelByOwner(OwnerId, CreatedAt.AddHours(1));

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.CancelByOwner(OwnerId, CreatedAt.AddHours(2)));

        Assert.Equal("request.cancellation_invalid_status", exception.Code);
        Assert.Equal(RequestStatus.Cancelled, request.Status);
        Assert.Equal(CreatedAt.AddHours(1), request.CompletedDate);
    }

    [Fact]
    public void CancelByOwner_WhenRequestIsRejected_ShouldRejectCancellation()
    {
        CorporateRequest request = SubmittedRequest();
        request.RejectByManager(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            ManagerId,
            "Ret",
            CreatedAt.AddHours(2));

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.CancelByOwner(OwnerId, CreatedAt.AddHours(3)));

        Assert.Equal("request.cancellation_invalid_status", exception.Code);
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal(CreatedAt.AddHours(2), request.CompletedDate);
        Assert.Single(request.Approvals);
    }

    [Fact]
    public void SoftDelete_WhenActorIsEmpty_ShouldPreserveDeletionState()
    {
        CorporateRequest request = CreateRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.SoftDelete(CreatedAt.AddHours(1), Guid.Empty));

        Assert.Equal("request.actor_required", exception.Code);
        Assert.False(request.IsDeleted);
        Assert.Null(request.DeletedDate);
        Assert.Null(request.DeletedByUserId);
        Assert.Equal(CreatedAt, request.UpdatedDate);
    }

    [Fact]
    public void Create_WhenTitleExceedsMaximumLength_ShouldRejectRequest()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CorporateRequest.Create(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                new string('a', CorporateRequest.TitleMaxLength + 1),
                "Açıklama",
                RequestType.Leave,
                null,
                OwnerId,
                DepartmentId,
                CreatedAt));

        Assert.Equal("request.title_too_long", exception.Code);
    }

    [Fact]
    public void Create_WhenTitleIsAtMaximumLength_ShouldAcceptRequest()
    {
        CorporateRequest request = CorporateRequest.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            new string('a', CorporateRequest.TitleMaxLength),
            "Açıklama",
            RequestType.Leave,
            null,
            OwnerId,
            DepartmentId,
            CreatedAt);

        Assert.Equal(CorporateRequest.TitleMaxLength, request.Title.Length);
    }

    [Fact]
    public void Create_WhenDescriptionIsAtMaximumLength_ShouldAcceptRequest()
    {
        CorporateRequest request = CorporateRequest.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Talep",
            new string('d', CorporateRequest.DescriptionMaxLength),
            RequestType.Leave,
            null,
            OwnerId,
            DepartmentId,
            CreatedAt);

        Assert.Equal(CorporateRequest.DescriptionMaxLength, request.Description.Length);
    }

    [Fact]
    public void Create_WhenDescriptionExceedsMaximumLength_ShouldRejectRequest()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CorporateRequest.Create(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Talep",
                new string('d', CorporateRequest.DescriptionMaxLength + 1),
                RequestType.Leave,
                null,
                OwnerId,
                DepartmentId,
                CreatedAt));

        Assert.Equal("request.description_too_long", exception.Code);
    }

    [Fact]
    public void UpdateDraft_WhenDescriptionExceedsMaximumLength_ShouldPreserveEntireState()
    {
        CorporateRequest request = CreateRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.UpdateDraft(
                OwnerId,
                "Changed title",
                new string('d', CorporateRequest.DescriptionMaxLength + 1),
                RequestType.Leave,
                null,
                CreatedAt.AddHours(1)));

        Assert.Equal("request.description_too_long", exception.Code);
        Assert.Equal("Test talebi", request.Title);
        Assert.Equal("Test açıklaması", request.Description);
        Assert.Equal(RequestType.Expense, request.RequestType);
        Assert.Equal(100, request.Amount);
        Assert.Equal(CreatedAt, request.UpdatedDate);
        Assert.Equal(RequestStatus.Draft, request.Status);
        Assert.Empty(request.Approvals);
    }

    [Fact]
    public void RejectByManager_WhenDescriptionExceedsMaximumLength_ShouldPreserveState()
    {
        CorporateRequest request = SubmittedRequest();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => request.RejectByManager(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                ManagerId,
                new string('x', CorporateRequest.RejectionDescriptionMaxLength + 1),
                CreatedAt.AddHours(2)));

        Assert.Equal("request.rejection_description_too_long", exception.Code);
        Assert.Equal(RequestStatus.PendingManagerApproval, request.Status);
        Assert.Empty(request.Approvals);
    }

    [Fact]
    public void RejectByManager_WhenDescriptionIsAtMaximumLength_ShouldAcceptRejection()
    {
        CorporateRequest request = SubmittedRequest();
        string description = new('x', CorporateRequest.RejectionDescriptionMaxLength);

        request.RejectByManager(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            ManagerId,
            description,
            CreatedAt.AddHours(2));

        Assert.Equal(description, Assert.Single(request.Approvals).Description);
        Assert.Equal(RequestStatus.Rejected, request.Status);
    }

    [Fact]
    public void Approvals_WhenExposed_ShouldNotAllowListCastOrExternalAdd()
    {
        CorporateRequest request = SubmittedRequest();
        request.ApproveByManager(Guid.Parse("55555555-5555-5555-5555-555555555555"), ManagerId, 10_000, CreatedAt.AddHours(2));

        Assert.IsType<ReadOnlyCollection<RequestApproval>>(request.Approvals);
        Assert.False(request.Approvals is List<RequestApproval>);
        ICollection<RequestApproval> collection = Assert.IsAssignableFrom<ICollection<RequestApproval>>(request.Approvals);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(request.Approvals.Single()));
        Assert.Single(request.Approvals);
    }

    [Fact]
    public void RowVersion_WhenReturnedArrayIsMutated_ShouldPreserveAggregateValue()
    {
        CorporateRequest request = CreateRequest();
        FieldInfo field = typeof(CorporateRequest).GetField("_rowVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(request, new byte[] { 1, 2, 3 });

        byte[] firstRead = request.RowVersion;
        firstRead[0] = 99;
        byte[] secondRead = request.RowVersion;

        Assert.Equal(new byte[] { 1, 2, 3 }, secondRead);
        Assert.NotSame(firstRead, secondRead);
    }

    private static CorporateRequest CreateRequest(RequestType requestType = RequestType.Expense, decimal? amount = 100)
    {
        return CorporateRequest.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Test talebi",
            "Test açıklaması",
            requestType,
            amount,
            OwnerId,
            DepartmentId,
            CreatedAt);
    }

    private static CorporateRequest SubmittedRequest(decimal? amount = 100, RequestType requestType = RequestType.Expense)
    {
        CorporateRequest request = CreateRequest(requestType, amount);
        request.Submit(OwnerId, CreatedAt.AddHours(1));
        return request;
    }

    private static CorporateRequest PendingAdminRequest()
    {
        CorporateRequest request = SubmittedRequest(10_001);
        request.ApproveByManager(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            ManagerId,
            10_000,
            CreatedAt.AddHours(2));
        return request;
    }
}
