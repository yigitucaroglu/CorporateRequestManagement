using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.UnitTests.Entities;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WhenUserIdIsNull_ShouldAcceptSystemAudit()
    {
        AuditLog audit = CreateAudit(null, "CorporateRequest", "Created");

        Assert.Null(audit.UserId);
        Assert.Equal("CorporateRequest", audit.EntityName);
        Assert.Equal("Created", audit.Action);
    }

    [Fact]
    public void Create_WhenUserIdIsEmpty_ShouldRejectAudit()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateAudit(Guid.Empty, "CorporateRequest", "Created"));

        Assert.Equal("audit.user_invalid", exception.Code);
    }

    [Fact]
    public void Create_WhenEntityNameAndActionAreAtMaximumLength_ShouldAcceptAudit()
    {
        AuditLog audit = CreateAudit(
            null,
            new string('e', AuditLog.EntityNameMaxLength),
            new string('a', AuditLog.ActionMaxLength));

        Assert.Equal(AuditLog.EntityNameMaxLength, audit.EntityName.Length);
        Assert.Equal(AuditLog.ActionMaxLength, audit.Action.Length);
    }

    [Fact]
    public void Create_WhenEntityNameExceedsMaximumLength_ShouldRejectAudit()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateAudit(null, new string('e', AuditLog.EntityNameMaxLength + 1), "Created"));

        Assert.Equal("audit.entity_name_too_long", exception.Code);
    }

    [Fact]
    public void Create_WhenActionExceedsMaximumLength_ShouldRejectAudit()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateAudit(null, "CorporateRequest", new string('a', AuditLog.ActionMaxLength + 1)));

        Assert.Equal("audit.action_too_long", exception.Code);
    }

    [Fact]
    public void Create_WhenOldValuesIsAtMaximumLength_ShouldAcceptAudit()
    {
        string oldValues = new('o', AuditLog.SerializedValuesMaxLength);

        AuditLog audit = CreateAudit(oldValues, null);

        Assert.Equal(oldValues, audit.OldValues);
    }

    [Fact]
    public void Create_WhenOldValuesExceedsMaximumLength_ShouldRejectAudit()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateAudit(new string('o', AuditLog.SerializedValuesMaxLength + 1), null));

        Assert.Equal("audit.old_values_too_long", exception.Code);
    }

    [Fact]
    public void Create_WhenNewValuesIsAtMaximumLength_ShouldAcceptAudit()
    {
        string newValues = new('n', AuditLog.SerializedValuesMaxLength);

        AuditLog audit = CreateAudit(null, newValues);

        Assert.Equal(newValues, audit.NewValues);
    }

    [Fact]
    public void Create_WhenNewValuesExceedsMaximumLength_ShouldRejectAudit()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateAudit(null, new string('n', AuditLog.SerializedValuesMaxLength + 1)));

        Assert.Equal("audit.new_values_too_long", exception.Code);
    }

    private static AuditLog CreateAudit(Guid? userId, string entityName, string action)
    {
        return AuditLog.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            userId,
            entityName,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            action,
            null,
            null,
            CreatedAt);
    }

    private static AuditLog CreateAudit(string? oldValues, string? newValues)
    {
        return AuditLog.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            null,
            "CorporateRequest",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Updated",
            oldValues,
            newValues,
            CreatedAt);
    }
}
