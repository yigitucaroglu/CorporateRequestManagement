using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorporateRequestManagement.Infrastructure.Persistence.Configurations;

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(department => department.Id);
        builder.Property(department => department.Id).ValueGeneratedNever();
        builder.Property(department => department.Name).IsRequired().HasMaxLength(Department.NameMaxLength);
        builder.Property(department => department.ManagerId).IsRequired(false);
        builder.Property(department => department.CreatedDate).IsRequired().HasColumnType("datetimeoffset");
        builder.Property(department => department.IsDeleted).IsRequired();
        builder.Property(department => department.DeletedDate).IsRequired(false).HasColumnType("datetimeoffset");
        builder.Property(department => department.DeletedByUserId).IsRequired(false);

        builder.HasQueryFilter(department => !department.IsDeleted);
        builder.HasIndex(department => department.Name)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Departments_Name_Active");
        builder.HasIndex(department => department.ManagerId)
            .HasDatabaseName("IX_Departments_ManagerId");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(department => department.ManagerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
