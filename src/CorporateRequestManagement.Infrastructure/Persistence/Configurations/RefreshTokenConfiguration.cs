using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorporateRequestManagement.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", table =>
            table.HasCheckConstraint("CK_RefreshTokens_Expiry", "[ExpiresAt] > [CreatedAt]"));
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.UserId).IsRequired();
        builder.Property(token => token.TokenHash).IsRequired().HasMaxLength(RefreshToken.TokenHashMaxLength).IsUnicode(false);
        builder.Property(token => token.ExpiresAt).IsRequired().HasColumnType("datetimeoffset");
        builder.Property(token => token.CreatedAt).IsRequired().HasColumnType("datetimeoffset");
        builder.Property(token => token.RevokedAt).IsRequired(false).HasColumnType("datetimeoffset");
        builder.Property(token => token.ReplacedByTokenId).IsRequired(false);
        builder.Property(token => token.CreatedByIp).IsRequired().HasMaxLength(RefreshToken.IpAddressMaxLength).IsUnicode(false);
        builder.Property(token => token.RevokedByIp).IsRequired(false).HasMaxLength(RefreshToken.IpAddressMaxLength).IsUnicode(false);

        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_TokenHash");
        builder.HasIndex(token => new { token.UserId, token.ExpiresAt, token.RevokedAt }).HasDatabaseName("IX_RefreshTokens_User_Expiry_Revoked");
        builder.HasIndex(token => token.ReplacedByTokenId).HasDatabaseName("IX_RefreshTokens_ReplacedByTokenId");
        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<RefreshToken>().WithMany().HasForeignKey(token => token.ReplacedByTokenId).OnDelete(DeleteBehavior.NoAction);
    }
}
