using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    RoomId = table.Column<long>(type: "bigint", nullable: false),
                    TimeRange = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.CheckConstraint("CK_Bookings_Status", "\"Status\" IN ('Confirmed', 'CheckedIn', 'Completed', 'Cancelled')");
                    table.CheckConstraint("CK_Bookings_TimeRange", "NOT isempty(\"TimeRange\") AND NOT lower_inf(\"TimeRange\") AND NOT upper_inf(\"TimeRange\") AND lower_inc(\"TimeRange\") AND NOT upper_inc(\"TimeRange\")");
                    table.ForeignKey(
                        name: "FK_Bookings_BookingRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_RequestId",
                table: "Bookings",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_RoomId",
                table: "Bookings",
                column: "RoomId");

            // Plan §8.2: PostgreSQL itself refuses two active bookings of one room whose [start, end) ranges overlap.
            // Adjacent ranges ([) touching) don't overlap. btree_gist (from the Facilities migration) supplies the
            // bigint "=" operator class for GiST. The name is literal (migrations never change) and matches
            // BookingConfiguration.NoRoomOverlapConstraint; a test checks they agree.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE "Bookings" ADD CONSTRAINT no_room_overlap
                  EXCLUDE USING gist ("RoomId" WITH =, "TimeRange" WITH &&)
                  WHERE ("Status" IN ('Confirmed', 'CheckedIn'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Bookings\" DROP CONSTRAINT IF EXISTS no_room_overlap;");

            migrationBuilder.DropTable(
                name: "Bookings");
        }
    }
}
