using CorporateRequestManagement.Domain.Common;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.Domain.Entities;

public sealed class RefreshToken : Entity
{
    public const int TokenHashMaxLength = 512;
    public const int IpAddressMaxLength = 64;

    private RefreshToken()
    {
    }

    private RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        string createdByIp)
        : base(id)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainRuleException("refresh_token.user_required", "Kullanıcı zorunludur.");
        }

        if (createdAt == default)
        {
            throw new DomainRuleException("refresh_token.created_date_required", "Oluşturulma zamanı zorunludur.");
        }

        if (expiresAt == default)
        {
            throw new DomainRuleException("refresh_token.expiry_required", "Sona erme zamanı zorunludur.");
        }

        TokenHash = RequiredText(tokenHash, TokenHashMaxLength, "refresh_token.hash_required", "refresh_token.hash_too_long", "Token özeti zorunludur.");
        if (expiresAt <= createdAt)
        {
            throw new DomainRuleException("refresh_token.expiry_invalid", "Token sona erme zamanı oluşturulma zamanından sonra olmalıdır.");
        }

        UserId = userId;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        CreatedByIp = RequiredText(createdByIp, IpAddressMaxLength, "refresh_token.created_ip_required", "refresh_token.created_ip_too_long", "Oluşturan IP zorunludur.");
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public string CreatedByIp { get; private set; } = string.Empty;

    public string? RevokedByIp { get; private set; }

    public static RefreshToken Create(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        string createdByIp)
    {
        return new RefreshToken(id, userId, tokenHash, expiresAt, createdAt, createdByIp);
    }

    public bool IsExpired(DateTimeOffset now)
    {
        return now >= ExpiresAt;
    }

    public bool IsActive(DateTimeOffset now)
    {
        return RevokedAt is null && !IsExpired(now);
    }

    public void Revoke(DateTimeOffset revokedAt, string revokedByIp, Guid? replacedByTokenId)
    {
        if (RevokedAt is not null)
        {
            throw new DomainRuleException("refresh_token.already_revoked", "Token daha önce iptal edilmiş.");
        }

        if (revokedAt < CreatedAt)
        {
            throw new DomainRuleException("refresh_token.revocation_date_invalid", "İptal zamanı oluşturulma zamanından önce olamaz.");
        }

        string validatedIp = RequiredText(
            revokedByIp,
            IpAddressMaxLength,
            "refresh_token.revoked_ip_required",
            "refresh_token.revoked_ip_too_long",
            "İptal eden IP zorunludur.");

        if (replacedByTokenId == Guid.Empty)
        {
            throw new DomainRuleException("refresh_token.replacement_invalid", "Yeni token kimliği boş olamaz.");
        }

        if (replacedByTokenId == Id)
        {
            throw new DomainRuleException("refresh_token.self_replacement_forbidden", "Token kendisiyle değiştirilemez.");
        }

        RevokedAt = revokedAt;
        RevokedByIp = validatedIp;
        ReplacedByTokenId = replacedByTokenId;
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
}
