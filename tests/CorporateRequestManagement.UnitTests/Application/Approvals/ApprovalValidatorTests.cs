using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Features.Approvals.Common;
using CorporateRequestManagement.Application.Features.Approvals.Decisions;
using CorporateRequestManagement.Application.Features.Approvals.Queries;
using CorporateRequestManagement.Domain.Entities;
using FluentValidation;

namespace CorporateRequestManagement.UnitTests.Application.Approvals;

public sealed class ApprovalValidatorTests
{
    private static readonly string ValidVersion = Convert.ToBase64String(new byte[8]);

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    public async Task Approve_when_row_version_is_invalid_should_fail(string rowVersion)
    {
        var command = new ManagerApproveCommand(Guid.NewGuid(), rowVersion);
        Assert.False((await ((IValidator<ManagerApproveCommand>)new ManagerApproveCommandValidator()).ValidateAsync(command)).IsValid);
    }

    [Fact]
    public async Task Approve_when_contract_is_valid_should_pass_for_both_levels()
    {
        Assert.True((await ((IValidator<ManagerApproveCommand>)new ManagerApproveCommandValidator()).ValidateAsync(new(Guid.NewGuid(), ValidVersion))).IsValid);
        Assert.True((await ((IValidator<AdminApproveCommand>)new AdminApproveCommandValidator()).ValidateAsync(new(Guid.NewGuid(), ValidVersion))).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_when_reason_is_missing_should_fail(string reason)
    {
        Assert.False((await ((IValidator<ManagerRejectCommand>)new ManagerRejectCommandValidator()).ValidateAsync(new(Guid.NewGuid(), ValidVersion, reason))).IsValid);
    }

    [Fact]
    public async Task Reject_when_reason_exceeds_domain_limit_should_fail_for_both_levels()
    {
        string reason = new('x', CorporateRequest.RejectionDescriptionMaxLength + 1);
        Assert.False((await ((IValidator<ManagerRejectCommand>)new ManagerRejectCommandValidator()).ValidateAsync(new(Guid.NewGuid(), ValidVersion, reason))).IsValid);
        Assert.False((await ((IValidator<AdminRejectCommand>)new AdminRejectCommandValidator()).ValidateAsync(new(Guid.NewGuid(), ValidVersion, reason))).IsValid);
    }

    [Fact]
    public async Task Queue_when_paging_sort_or_date_range_is_invalid_should_fail()
    {
        DateTimeOffset now = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var query = new GetManagerApprovalQueueQuery(0, 101, CreatedFrom: now, CreatedTo: now.AddMinutes(-1), SortBy: "unsupported", SortDirection: (SortDirection)999);
        Assert.False((await ((IValidator<GetManagerApprovalQueueQuery>)new GetManagerApprovalQueueQueryValidator()).ValidateAsync(query)).IsValid);
    }

    [Fact]
    public async Task Queue_defaults_and_whitelisted_sort_should_pass()
    {
        Assert.True((await ((IValidator<GetManagerApprovalQueueQuery>)new GetManagerApprovalQueueQueryValidator()).ValidateAsync(new())).IsValid);
        Assert.True((await ((IValidator<GetAdminApprovalQueueQuery>)new GetAdminApprovalQueueQueryValidator()).ValidateAsync(
            new(SortBy: ApprovalSortFields.Amount, SortDirection: SortDirection.Descending))).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("normal arama", true)]
    public async Task Queue_search_should_accept_optional_and_normal_values(string? search, bool expected)
    {
        var result = await ((IValidator<GetManagerApprovalQueueQuery>)new GetManagerApprovalQueueQueryValidator())
            .ValidateAsync(new(SearchTerm: search));
        Assert.Equal(expected, result.IsValid);
    }

    [Fact]
    public async Task Queue_search_should_enforce_shared_maximum()
    {
        Assert.True((await ((IValidator<GetAdminApprovalQueueQuery>)new GetAdminApprovalQueueQueryValidator())
            .ValidateAsync(new(SearchTerm: new string('a', ApprovalSearch.MaxLength)))).IsValid);
        Assert.False((await ((IValidator<GetAdminApprovalQueueQuery>)new GetAdminApprovalQueueQueryValidator())
            .ValidateAsync(new(SearchTerm: new string('a', ApprovalSearch.MaxLength + 1)))).IsValid);
        Assert.Null(ApprovalSearch.Normalize("   "));
    }
}
