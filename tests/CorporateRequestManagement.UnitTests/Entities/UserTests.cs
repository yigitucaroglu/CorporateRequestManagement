using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.UnitTests.Entities;

public sealed class UserTests
{
    private static readonly Guid DepartmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AdminId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WhenEmployeeHasNoDepartment_ShouldRejectUser()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateUser(UserRole.Employee, null));

        Assert.Equal("user.department_required", exception.Code);
    }

    [Fact]
    public void Create_WhenAdminHasNoDepartment_ShouldCreateUser()
    {
        User user = CreateUser(UserRole.Admin, null);

        Assert.Null(user.DepartmentId);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void Create_WhenValuesContainWhitespace_ShouldTrimAndNormalizeUser()
    {
        User user = User.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "  Ada  ",
            "  Lovelace  ",
            "  ADA@EXAMPLE.COM  ",
            "  hashed-password  ",
            UserRole.Employee,
            DepartmentId,
            CreatedAt);

        Assert.Equal("Ada", user.FirstName);
        Assert.Equal("Lovelace", user.LastName);
        Assert.Equal("ada@example.com", user.Email);
        Assert.Equal("hashed-password", user.PasswordHash);
    }

    [Fact]
    public void SoftDelete_WhenUserIsActive_ShouldDeactivateAndSetMetadata()
    {
        User user = CreateUser(UserRole.Employee, DepartmentId);
        DateTimeOffset deletedAt = CreatedAt.AddDays(1);

        user.SoftDelete(deletedAt, AdminId);

        Assert.False(user.IsActive);
        Assert.True(user.IsDeleted);
        Assert.Equal(deletedAt, user.DeletedDate);
        Assert.Equal(AdminId, user.DeletedByUserId);
    }

    [Fact]
    public void SoftDelete_WhenUserWasAlreadyDeleted_ShouldRejectSecondDeletion()
    {
        User user = CreateUser(UserRole.Employee, DepartmentId);
        user.SoftDelete(CreatedAt.AddDays(1), AdminId);

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => user.SoftDelete(CreatedAt.AddDays(2), AdminId));

        Assert.Equal("entity.already_deleted", exception.Code);
    }

    [Fact]
    public void Create_WhenEmployeeDepartmentIsEmpty_ShouldRejectUser()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateUser(UserRole.Employee, Guid.Empty));

        Assert.Equal("user.department_invalid", exception.Code);
    }

    [Fact]
    public void Create_WhenEmailFormatIsInvalid_ShouldRejectUser()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => User.Create(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Ada",
                "Lovelace",
                "invalid-email",
                "hashed-password",
                UserRole.Employee,
                DepartmentId,
                CreatedAt));

        Assert.Equal("user.email_invalid", exception.Code);
    }

    [Fact]
    public void Create_WhenEmailIsAtMaximumLengthAndValid_ShouldAcceptUser()
    {
        string email = $"a@{new string('d', User.EmailMaxLength - 2)}";

        User user = User.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Ada",
            "Lovelace",
            email,
            "hashed-password",
            UserRole.Employee,
            DepartmentId,
            CreatedAt);

        Assert.Equal(User.EmailMaxLength, user.Email.Length);
    }

    [Fact]
    public void Create_WhenEmailExceedsMaximumLength_ShouldRejectUser()
    {
        string email = $"a@{new string('d', User.EmailMaxLength - 1)}";

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => User.Create(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Ada",
                "Lovelace",
                email,
                "hashed-password",
                UserRole.Employee,
                DepartmentId,
                CreatedAt));

        Assert.Equal("user.email_too_long", exception.Code);
    }

    [Fact]
    public void Create_WhenRoleIsInvalid_ShouldRejectUser()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateUser((UserRole)999, DepartmentId));

        Assert.Equal("user.role_invalid", exception.Code);
    }

    [Fact]
    public void Create_WhenFirstNameIsAtMaximumLength_ShouldAcceptUser()
    {
        User user = CreateUserWithText(
            new string('f', User.NameMaxLength),
            "Lovelace",
            "hashed-password");

        Assert.Equal(User.NameMaxLength, user.FirstName.Length);
    }

    [Fact]
    public void Create_WhenFirstNameExceedsMaximumLength_ShouldRejectUser()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateUserWithText(
                new string('f', User.NameMaxLength + 1),
                "Lovelace",
                "hashed-password"));

        Assert.Equal("user.first_name_too_long", exception.Code);
    }

    [Fact]
    public void Create_WhenLastNameIsAtMaximumLength_ShouldAcceptUser()
    {
        User user = CreateUserWithText(
            "Ada",
            new string('l', User.NameMaxLength),
            "hashed-password");

        Assert.Equal(User.NameMaxLength, user.LastName.Length);
    }

    [Fact]
    public void Create_WhenLastNameExceedsMaximumLength_ShouldRejectUser()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateUserWithText(
                "Ada",
                new string('l', User.NameMaxLength + 1),
                "hashed-password"));

        Assert.Equal("user.last_name_too_long", exception.Code);
    }

    [Fact]
    public void Create_WhenPasswordHashIsAtMaximumLength_ShouldAcceptUser()
    {
        User user = CreateUserWithText(
            "Ada",
            "Lovelace",
            new string('h', User.PasswordHashMaxLength));

        Assert.Equal(User.PasswordHashMaxLength, user.PasswordHash.Length);
    }

    [Fact]
    public void Create_WhenPasswordHashExceedsMaximumLength_ShouldRejectUser()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => CreateUserWithText(
                "Ada",
                "Lovelace",
                new string('h', User.PasswordHashMaxLength + 1)));

        Assert.Equal("user.password_hash_too_long", exception.Code);
    }

    [Fact]
    public void SoftDelete_WhenActorIsEmpty_ShouldPreserveActiveState()
    {
        User user = CreateUser(UserRole.Employee, DepartmentId);

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => user.SoftDelete(CreatedAt.AddDays(1), Guid.Empty));

        Assert.Equal("entity.deleted_by_required", exception.Code);
        Assert.False(user.IsDeleted);
        Assert.Null(user.DeletedDate);
        Assert.Null(user.DeletedByUserId);
        Assert.True(user.IsActive);
    }

    private static User CreateUser(UserRole role, Guid? departmentId)
    {
        return User.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Ada",
            "Lovelace",
            "ada@example.com",
            "hashed-password",
            role,
            departmentId,
            CreatedAt);
    }

    private static User CreateUserWithText(string firstName, string lastName, string passwordHash)
    {
        return User.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            firstName,
            lastName,
            "ada@example.com",
            passwordHash,
            UserRole.Employee,
            DepartmentId,
            CreatedAt);
    }
}
