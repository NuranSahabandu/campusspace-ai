using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        var allowedRoles = string.Join(", ", Roles.All.Select(r => $"'{r}'"));
        builder.ToTable(t => t.HasCheckConstraint("CK_Users_Role", $"\"Role\" IN ({allowedRoles})"));

        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.Role).IsRequired().HasMaxLength(32);

        // Sentinel true: EF omits the column only when the value is still true, so an explicit false is saved.
        builder.Property(u => u.IsActive).HasDefaultValue(true).HasSentinel(true);
    }
}
