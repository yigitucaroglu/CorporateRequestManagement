using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.Domain.Common;

public abstract class SoftDeletableEntity : Entity
{
    protected SoftDeletableEntity()
    {
    }

    protected SoftDeletableEntity(Guid id)
        : base(id)
    {
    }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedDate { get; private set; }

    public Guid? DeletedByUserId { get; private set; }

    protected void MarkDeleted(DateTimeOffset deletedDate, Guid deletedByUserId)
    {
        if (IsDeleted)
        {
            throw new DomainRuleException("entity.already_deleted", "Kayıt daha önce silinmiş.");
        }

        if (deletedByUserId == Guid.Empty)
        {
            throw new DomainRuleException("entity.deleted_by_required", "Silen kullanıcı kimliği boş olamaz.");
        }

        IsDeleted = true;
        DeletedDate = deletedDate;
        DeletedByUserId = deletedByUserId;
    }
}
