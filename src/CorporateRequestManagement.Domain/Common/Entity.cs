using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.Domain.Common;

public abstract class Entity
{
    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleException("entity.id_required", "Kimlik boş olamaz.");
        }

        Id = id;
    }

    public Guid Id { get; private set; }
}
