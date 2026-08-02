using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorporateRequestManagement.Infrastructure.Persistence.Configurations;

internal sealed class RequestApprovalConfiguration : IEntityTypeConfiguration<RequestApproval>
{
    public void Configure(EntityTypeBuilder<RequestApproval> builder)
    {
        builder.ToTable("RequestApprovals");
        builder.HasKey(approval => approval.Id);
        builder.Property(approval => approval.Id).ValueGeneratedNever();
        builder.Property(approval => approval.RequestId).IsRequired();
        builder.Property(approval => approval.ApprovedByUserId).IsRequired();
        builder.Property(approval => approval.ApprovalLevel).IsRequired().HasConversion<int>();
        builder.Property(approval => approval.Status).IsRequired().HasConversion<int>();
        builder.Property(approval => approval.Description).IsRequired(false).HasMaxLength(CorporateRequest.RejectionDescriptionMaxLength);
        builder.Property(approval => approval.ActionDate).IsRequired().HasColumnType("datetimeoffset");

        builder.HasIndex(approval => new { approval.RequestId, approval.ApprovalLevel })
            .IsUnique()
            .HasDatabaseName("UX_RequestApprovals_Request_Level");
        builder.HasIndex(approval => new { approval.RequestId, approval.ApprovalLevel, approval.ActionDate })
            .HasDatabaseName("IX_RequestApprovals_Request_Level_ActionDate");
        builder.HasOne<User>().WithMany().HasForeignKey(approval => approval.ApprovedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
