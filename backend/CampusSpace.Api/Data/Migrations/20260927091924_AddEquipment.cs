using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Features_Code",
                table: "Features");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Features_Code",
                table: "Features",
                column: "Code");

            migrationBuilder.CreateTable(
                name: "EquipmentTypes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FeePerBooking = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    CoveredByFeatureCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentTypes", x => x.Id);
                    table.CheckConstraint("CK_EquipmentTypes_Category", "\"Category\" IN ('Audio', 'Visual', 'Computing', 'Presentation', 'Accessory')");
                    table.CheckConstraint("CK_EquipmentTypes_Code_Format", "\"Code\" ~ '^[A-Z0-9]+(-[A-Z0-9]+)*$' AND char_length(\"Code\") >= 2");
                    table.CheckConstraint("CK_EquipmentTypes_FeePerBooking", "\"FeePerBooking\" >= 0");
                    table.CheckConstraint("CK_EquipmentTypes_Name_NotBlank", "btrim(\"Name\") <> ''");
                    table.ForeignKey(
                        name: "FK_EquipmentTypes_Features_CoveredByFeatureCode",
                        column: x => x.CoveredByFeatureCode,
                        principalTable: "Features",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TypeId = table.Column<long>(type: "bigint", nullable: false),
                    AssetTag = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentItems", x => x.Id);
                    table.CheckConstraint("CK_EquipmentItems_AssetTag_NotBlank", "btrim(\"AssetTag\") <> ''");
                    table.CheckConstraint("CK_EquipmentItems_Condition", "\"Condition\" IN ('Good', 'MinorWear', 'Damaged')");
                    table.CheckConstraint("CK_EquipmentItems_Status", "\"Status\" IN ('Available', 'OnLoan', 'UnderRepair', 'Retired')");
                    table.ForeignKey(
                        name: "FK_EquipmentItems_EquipmentTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "EquipmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentSubstitutes",
                columns: table => new
                {
                    TypeId = table.Column<long>(type: "bigint", nullable: false),
                    SubstituteTypeId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentSubstitutes", x => new { x.TypeId, x.SubstituteTypeId });
                    table.CheckConstraint("CK_EquipmentSubstitutes_NotSelf", "\"TypeId\" <> \"SubstituteTypeId\"");
                    table.ForeignKey(
                        name: "FK_EquipmentSubstitutes_EquipmentTypes_SubstituteTypeId",
                        column: x => x.SubstituteTypeId,
                        principalTable: "EquipmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipmentSubstitutes_EquipmentTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "EquipmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItems_AssetTag",
                table: "EquipmentItems",
                column: "AssetTag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentItems_TypeId_Status",
                table: "EquipmentItems",
                columns: new[] { "TypeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentSubstitutes_SubstituteTypeId",
                table: "EquipmentSubstitutes",
                column: "SubstituteTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentTypes_Code",
                table: "EquipmentTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentTypes_CoveredByFeatureCode",
                table: "EquipmentTypes",
                column: "CoveredByFeatureCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentItems");

            migrationBuilder.DropTable(
                name: "EquipmentSubstitutes");

            migrationBuilder.DropTable(
                name: "EquipmentTypes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Features_Code",
                table: "Features");

            migrationBuilder.CreateIndex(
                name: "IX_Features_Code",
                table: "Features",
                column: "Code",
                unique: true);
        }
    }
}
