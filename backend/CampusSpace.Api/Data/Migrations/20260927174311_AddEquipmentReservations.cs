using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentReservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    TypeId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    TimeRange = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentReservations", x => x.Id);
                    table.CheckConstraint("CK_EquipmentReservations_Quantity", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_EquipmentReservations_TimeRange", "NOT isempty(\"TimeRange\") AND NOT lower_inf(\"TimeRange\") AND NOT upper_inf(\"TimeRange\") AND lower_inc(\"TimeRange\") AND NOT upper_inc(\"TimeRange\")");
                    table.ForeignKey(
                        name: "FK_EquipmentReservations_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipmentReservations_EquipmentTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "EquipmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReservations_BookingId_TypeId",
                table: "EquipmentReservations",
                columns: new[] { "BookingId", "TypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReservations_TypeId_TimeRange",
                table: "EquipmentReservations",
                columns: new[] { "TypeId", "TimeRange" })
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentReservations");
        }
    }
}
