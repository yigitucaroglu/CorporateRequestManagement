using CorporateRequestManagement.Domain.Common;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.Domain.Entities;

public sealed class Department : SoftDeletableEntity
{
    public const int NameMaxLength = 150;

    private Department()
    {
    }

    private Department(Guid id, string name, Guid? managerId, DateTimeOffset createdDate)
        : base(id)
    {
        if (createdDate == default)
        {
            throw new DomainRuleException("department.created_date_required", "Oluşturulma zamanı zorunludur.");
        }

        if (managerId == Guid.Empty)
        {
            throw new DomainRuleException("department.manager_invalid", "Yönetici kimliği boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("department.name_required", "Departman adı zorunludur.");
        }

        string trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
        {
            throw new DomainRuleException("department.name_too_long", "Departman adı izin verilen uzunluğu aşıyor.");
        }

        Name = trimmedName;
        ManagerId = managerId;
        CreatedDate = createdDate;
    }

    public string Name { get; private set; } = string.Empty;

    public Guid? ManagerId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public static Department Create(Guid id, string name, Guid? managerId, DateTimeOffset createdDate)
    {
        return new Department(id, name, managerId, createdDate);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("department.name_required", "Departman adı zorunludur.");
        string trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength) throw new DomainRuleException("department.name_too_long", "Departman adı izin verilen uzunluğu aşıyor.");
        Name = trimmed;
    }

    public void AssignManager(Guid? managerId)
    {
        if (managerId == Guid.Empty) throw new DomainRuleException("department.manager_invalid", "Yönetici kimliği boş olamaz.");
        ManagerId = managerId;
    }

    public void SoftDelete(DateTimeOffset deletedDate, Guid deletedByUserId)
    {
        if (deletedDate < CreatedDate)
        {
            throw new DomainRuleException("department.deletion_date_invalid", "Silinme zamanı oluşturulma zamanından önce olamaz.");
        }

        MarkDeleted(deletedDate, deletedByUserId);
    }
}
