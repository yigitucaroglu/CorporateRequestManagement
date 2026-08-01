using CorporateRequestManagement.Domain.Common;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.Domain.Entities;

public sealed class AuditLog : Entity
{
    public const int EntityNameMaxLength = 200;
    public const int ActionMaxLength = 100;
    public const int SerializedValuesMaxLength = 32000;

    private AuditLog()
    {
    }

    private AuditLog(
        Guid id,
        Guid? userId,
        string entityName,
        Guid entityId,
        string action,
        string? oldValues,
        string? newValues,
        DateTimeOffset createdDate)
        : base(id)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainRuleException("audit.user_invalid", "Kullanıcı kimliği boş olamaz.");
        }

        if (entityId == Guid.Empty)
        {
            throw new DomainRuleException("audit.entity_id_required", "Denetim kaydı varlık kimliği zorunludur.");
        }

        if (createdDate == default)
        {
            throw new DomainRuleException("audit.created_date_required", "Denetim kaydı zamanı zorunludur.");
        }

        UserId = userId;
        EntityName = RequiredText(entityName, EntityNameMaxLength, "audit.entity_name_required", "audit.entity_name_too_long", "Varlık adı zorunludur.");
        EntityId = entityId;
        Action = RequiredText(action, ActionMaxLength, "audit.action_required", "audit.action_too_long", "İşlem adı zorunludur.");
        OldValues = OptionalText(oldValues, "audit.old_values_too_long");
        NewValues = OptionalText(newValues, "audit.new_values_too_long");
        CreatedDate = createdDate;
    }

    public Guid? UserId { get; private set; }

    public string EntityName { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public static AuditLog Create(
        Guid id,
        Guid? userId,
        string entityName,
        Guid entityId,
        string action,
        string? oldValues,
        string? newValues,
        DateTimeOffset createdDate)
    {
        return new AuditLog(id, userId, entityName, entityId, action, oldValues, newValues, createdDate);
    }

    private static string RequiredText(string value, int maxLength, string requiredCode, string lengthCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException(requiredCode, message);
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainRuleException(lengthCode, "Alan izin verilen uzunluğu aşıyor.");
        }

        return trimmed;
    }

    private static string? OptionalText(string? value, string lengthCode)
    {
        string? trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > SerializedValuesMaxLength)
        {
            throw new DomainRuleException(lengthCode, "Serileştirilmiş değer izin verilen uzunluğu aşıyor.");
        }

        return trimmed;
    }
}
