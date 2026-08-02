using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorporateRequestManagement.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Id).ValueGeneratedNever();
        builder.Property(audit => audit.UserId).IsRequired(false);
        builder.Property(audit => audit.EntityName).IsRequired().HasMaxLength(AuditLog.EntityNameMaxLength);
        builder.Property(audit => audit.EntityId).IsRequired();
        builder.Property(audit => audit.Action).IsRequired().HasMaxLength(AuditLog.ActionMaxLength);
        builder.Property(audit => audit.OldValues).IsRequired(false).HasMaxLength(AuditLog.SerializedValuesMaxLength);
        builder.Property(audit => audit.NewValues).IsRequired(false).HasMaxLength(AuditLog.SerializedValuesMaxLength);
        builder.Property(audit => audit.CreatedDate).IsRequired().HasColumnType("datetimeoffset");

        builder.HasIndex(audit => new { audit.EntityName, audit.EntityId, audit.CreatedDate }).HasDatabaseName("IX_AuditLogs_Entity_CreatedDate");
        builder.HasIndex(audit => new { audit.UserId, audit.CreatedDate }).HasDatabaseName("IX_AuditLogs_User_CreatedDate");
        builder.HasOne<User>().WithMany().HasForeignKey(audit => audit.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
