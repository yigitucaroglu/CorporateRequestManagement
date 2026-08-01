using CorporateRequestManagement.Domain.Common;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.Domain.Entities;

public sealed class User : SoftDeletableEntity
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int PasswordHashMaxLength = 512;

    private User()
    {
    }

    private User(
        Guid id,
        string firstName,
        string lastName,
        string email,
        string passwordHash,
        UserRole role,
        Guid? departmentId,
        DateTimeOffset createdDate)
        : base(id)
    {
        FirstName = RequiredText(firstName, NameMaxLength, "user.first_name_required", "user.first_name_too_long");
        LastName = RequiredText(lastName, NameMaxLength, "user.last_name_required", "user.last_name_too_long");
        string normalizedEmail = RequiredText(email, EmailMaxLength, "user.email_required", "user.email_too_long").ToLowerInvariant();
        if (!IsValidEmail(normalizedEmail))
        {
            throw new DomainRuleException("user.email_invalid", "E-posta biçimi geçersiz.");
        }

        Email = normalizedEmail;
        PasswordHash = RequiredText(passwordHash, PasswordHashMaxLength, "user.password_hash_required", "user.password_hash_too_long");

        if (createdDate == default)
        {
            throw new DomainRuleException("user.created_date_required", "Oluşturulma zamanı zorunludur.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new DomainRuleException("user.role_invalid", "Kullanıcı rolü geçersiz.");
        }

        if (departmentId == Guid.Empty)
        {
            throw new DomainRuleException("user.department_invalid", "Departman kimliği boş olamaz.");
        }

        if (role is UserRole.Employee or UserRole.Manager && departmentId is null)
        {
            throw new DomainRuleException("user.department_required", "Çalışan ve yönetici için departman zorunludur.");
        }

        Role = role;
        DepartmentId = departmentId;
        IsActive = true;
        CreatedDate = createdDate;
    }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public static User Create(
        Guid id,
        string firstName,
        string lastName,
        string email,
        string passwordHash,
        UserRole role,
        Guid? departmentId,
        DateTimeOffset createdDate)
    {
        return new User(id, firstName, lastName, email, passwordHash, role, departmentId, createdDate);
    }

    public void SoftDelete(DateTimeOffset deletedDate, Guid deletedByUserId)
    {
        if (deletedDate < CreatedDate)
        {
            throw new DomainRuleException("user.deletion_date_invalid", "Silinme zamanı oluşturulma zamanından önce olamaz.");
        }

        MarkDeleted(deletedDate, deletedByUserId);
        IsActive = false;
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

    private static bool IsValidEmail(string email)
    {
        if (email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        int separatorIndex = email.IndexOf('@');
        return separatorIndex > 0
            && separatorIndex == email.LastIndexOf('@')
            && separatorIndex < email.Length - 1;
    }
}
