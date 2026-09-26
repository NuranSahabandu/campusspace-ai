using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class RoomFeatureConfiguration : IEntityTypeConfiguration<RoomFeature>
{
    public void Configure(EntityTypeBuilder<RoomFeature> builder)
    {
        builder.HasKey(rf => new { rf.RoomId, rf.FeatureId });

        // CASCADE: a room's feature rows mean nothing without the room. RESTRICT: a feature in use cannot be deleted (409 "In use").
        builder.HasOne(rf => rf.Room).WithMany(r => r.RoomFeatures).HasForeignKey(rf => rf.RoomId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(rf => rf.Feature).WithMany().HasForeignKey(rf => rf.FeatureId).OnDelete(DeleteBehavior.Restrict);

        // RoomId is the leading PK column, so only FeatureId needs its own FK index.
        builder.HasIndex(rf => rf.FeatureId);
    }
}
