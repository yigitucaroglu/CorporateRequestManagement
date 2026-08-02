using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorporateRequestManagement.Infrastructure.Persistence.Configurations;

internal sealed class CorporateRequestConfiguration : IEntityTypeConfiguration<CorporateRequest>
{
    public void Configure(EntityTypeBuilder<CorporateRequest> builder)
    {
        builder.ToTable("CorporateRequests", table =>
            table.HasCheckConstraint("CK_CorporateRequests_Amount_Positive", "[Amount] IS NULL OR [Amount] > 0"));
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.Title).IsRequired().HasMaxLength(CorporateRequest.TitleMaxLength);
        builder.Property(request => request.Description).IsRequired().HasMaxLength(CorporateRequest.DescriptionMaxLength);
        builder.Property(request => request.RequestType).IsRequired().HasConversion<int>();
        builder.Property(request => request.Amount).IsRequired(false).HasPrecision(18, 2);
        builder.Property(request => request.Status).IsRequired().HasConversion<int>();
        builder.Property(request => request.CreatedByUserId).IsRequired();
        builder.Property(request => request.DepartmentId).IsRequired();
        builder.Property(request => request.CreatedDate).IsRequired().HasColumnType("datetimeoffset");
        builder.Property(request => request.UpdatedDate).IsRequired().HasColumnType("datetimeoffset");
        builder.Property(request => request.SubmittedDate).IsRequired(false).HasColumnType("datetimeoffset");
        builder.Property(request => request.CompletedDate).IsRequired(false).HasColumnType("datetimeoffset");
        builder.Property(request => request.IsDeleted).IsRequired();
        builder.Property(request => request.DeletedDate).IsRequired(false).HasColumnType("datetimeoffset");
        builder.Property(request => request.DeletedByUserId).IsRequired(false);

        builder.Ignore(request => request.RowVersion);
        builder.Property<byte[]>("_rowVersion")
            .HasColumnName("RowVersion")
            .IsRequired()
            .IsRowVersion()
            .IsConcurrencyToken()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasQueryFilter(request => !request.IsDeleted);
        builder.HasIndex(request => new { request.CreatedByUserId, request.Status, request.CreatedDate })
            .HasDatabaseName("IX_CorporateRequests_Owner_Status_CreatedDate");
        builder.HasIndex(request => new { request.DepartmentId, request.Status, request.CreatedDate })
            .HasDatabaseName("IX_CorporateRequests_Department_Status_CreatedDate");
        builder.HasIndex(request => new { request.RequestType, request.Amount })
            .HasDatabaseName("IX_CorporateRequests_Type_Amount");

        builder.HasOne<User>().WithMany().HasForeignKey(request => request.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Department>().WithMany().HasForeignKey(request => request.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(request => request.Approvals)
            .WithOne()
            .HasForeignKey(approval => approval.RequestId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.Navigation(request => request.Approvals).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
