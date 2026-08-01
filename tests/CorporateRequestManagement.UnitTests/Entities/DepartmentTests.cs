using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.UnitTests.Entities;

public sealed class DepartmentTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WhenManagerIdIsEmpty_ShouldRejectDepartment()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => Department.Create(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Finans",
                Guid.Empty,
                CreatedAt));

        Assert.Equal("department.manager_invalid", exception.Code);
    }

    [Fact]
    public void Create_WhenNameIsAtMaximumLength_ShouldAcceptDepartment()
    {
        Department department = Department.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new string('n', Department.NameMaxLength),
            null,
            CreatedAt);

        Assert.Equal(Department.NameMaxLength, department.Name.Length);
        Assert.Null(department.ManagerId);
    }

    [Fact]
    public void Create_WhenNameExceedsMaximumLength_ShouldRejectDepartment()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => Department.Create(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new string('n', Department.NameMaxLength + 1),
                null,
                CreatedAt));

        Assert.Equal("department.name_too_long", exception.Code);
    }

    [Fact]
    public void SoftDelete_WhenDateIsBeforeCreation_ShouldPreserveStateAndManager()
    {
        Guid managerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Department department = Department.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Finans",
            managerId,
            CreatedAt);

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => department.SoftDelete(CreatedAt.AddTicks(-1), Guid.Parse("33333333-3333-3333-3333-333333333333")));

        Assert.Equal("department.deletion_date_invalid", exception.Code);
        Assert.False(department.IsDeleted);
        Assert.Null(department.DeletedDate);
        Assert.Equal(managerId, department.ManagerId);
    }

    [Fact]
    public void SoftDelete_WhenActorIsEmpty_ShouldPreserveStateAndManager()
    {
        Guid managerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Department department = Department.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Finans",
            managerId,
            CreatedAt);

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => department.SoftDelete(CreatedAt.AddDays(1), Guid.Empty));

        Assert.Equal("entity.deleted_by_required", exception.Code);
        Assert.False(department.IsDeleted);
        Assert.Null(department.DeletedDate);
        Assert.Null(department.DeletedByUserId);
        Assert.Equal(managerId, department.ManagerId);
    }
}
