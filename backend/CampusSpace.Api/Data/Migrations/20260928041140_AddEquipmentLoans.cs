using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentLoans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    CheckedOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CheckedOutById = table.Column<long>(type: "bigint", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CheckedInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckedInById = table.Column<long>(type: "bigint", nullable: true),
                    ReturnCondition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DamageNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DamagePhotoPath = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsLateReturn = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentLoans", x => x.Id);
                    table.CheckConstraint("CK_EquipmentLoans_CheckedInAt", "\"CheckedInAt\" >= \"CheckedOutAt\"");
                    table.CheckConstraint("CK_EquipmentLoans_CheckIn", "(\"CheckedInAt\" IS NULL AND \"CheckedInById\" IS NULL AND \"ReturnCondition\" IS NULL AND \"DamageNote\" IS NULL AND \"DamagePhotoPath\" IS NULL AND NOT \"IsLateReturn\") OR (\"CheckedInAt\" IS NOT NULL AND \"CheckedInById\" IS NOT NULL AND \"ReturnCondition\" IS NOT NULL)");
                    table.CheckConstraint("CK_EquipmentLoans_Damaged", "\"ReturnCondition\" IS DISTINCT FROM 'Damaged' OR (\"DamageNote\" IS NOT NULL AND \"DamagePhotoPath\" IS NOT NULL)");
                    table.CheckConstraint("CK_EquipmentLoans_DamagePhotoPath", "\"DamagePhotoPath\" ~ '^[0-9a-f]{32}\\.(jpg|png)$'");
                    table.CheckConstraint("CK_EquipmentLoans_DueAt", "\"DueAt\" > \"CheckedOutAt\"");
                    table.CheckConstraint("CK_EquipmentLoans_ReturnCondition", "\"ReturnCondition\" IN ('Good', 'MinorWear', 'Damaged')");
                    table.ForeignKey(
                        name: "FK_EquipmentLoans_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentLoans_EquipmentItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "EquipmentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentLoans_Users_CheckedInById",
                        column: x => x.CheckedInById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentLoans_Users_CheckedOutById",
                        column: x => x.CheckedOutById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoans_BookingId",
                table: "EquipmentLoans",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoans_CheckedInById",
                table: "EquipmentLoans",
                column: "CheckedInById");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoans_CheckedOutById",
                table: "EquipmentLoans",
                column: "CheckedOutById");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoans_DueAt_Open",
                table: "EquipmentLoans",
                column: "DueAt",
                filter: "\"CheckedInAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoans_ItemId",
                table: "EquipmentLoans",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLoans_ItemId_Open",
                table: "EquipmentLoans",
                column: "ItemId",
                unique: true,
                filter: "\"CheckedInAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentLoans");
        }
    }
}
