using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorporateRequestManagement.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.FirstName).IsRequired().HasMaxLength(User.NameMaxLength);
        builder.Property(user => user.LastName).IsRequired().HasMaxLength(User.NameMaxLength);
        builder.Property(user => user.Email).IsRequired().HasMaxLength(User.EmailMaxLength);
        builder.Property(user => user.PasswordHash).IsRequired().HasMaxLength(User.PasswordHashMaxLength).IsUnicode(false);
        builder.Property(user => user.Role).IsRequired().HasConversion<int>();
        builder.Property(user => user.DepartmentId).IsRequired(false);
        builder.Property(user => user.IsActive).IsRequired();
        builder.Property(user => user.CreatedDate).IsRequired().HasColumnType("datetimeoffset");
        builder.Property(user => user.IsDeleted).IsRequired();
        builder.Property(user => user.DeletedDate).IsRequired(false).HasColumnType("datetimeoffset");
        builder.Property(user => user.DeletedByUserId).IsRequired(false);

        builder.HasQueryFilter(user => !user.IsDeleted);
        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Users_Email_Active");
        builder.HasIndex(user => new { user.DepartmentId, user.Role, user.IsActive })
            .HasDatabaseName("IX_Users_Department_Role_Active");

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(user => user.DepartmentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
