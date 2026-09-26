using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        var allowedTypes = string.Join(", ", RoomTypes.All.Select(t => $"'{t}'"));
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Rooms_Type", $"\"Type\" IN ({allowedTypes})");
            t.HasCheckConstraint("CK_Rooms_Capacity", "\"Capacity\" > 0");
            t.HasCheckConstraint("CK_Rooms_Code_NotBlank", "btrim(\"Code\") <> ''");
            t.HasCheckConstraint("CK_Rooms_Name_NotBlank", "btrim(\"Name\") <> ''");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Code).IsRequired().HasMaxLength(20);
        builder.HasIndex(r => r.Code).IsUnique();
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Type).IsRequired().HasMaxLength(20);
        builder.HasIndex(r => r.Capacity);

        // Sentinel true: EF omits the column only when the value is still true, so an explicit false is saved.
        builder.Property(r => r.IsActive).HasDefaultValue(true).HasSentinel(true);

        builder.HasOne(r => r.Building).WithMany(b => b.Rooms).HasForeignKey(r => r.BuildingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.BuildingId);
    }
}
