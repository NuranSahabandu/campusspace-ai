using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Quotations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    DiscountReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsExempt = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotations", x => x.Id);
                    table.CheckConstraint("CK_Quotations_Discount", "\"Discount\" >= 0");
                    table.CheckConstraint("CK_Quotations_Discount_LE_Subtotal", "\"Discount\" <= \"Subtotal\"");
                    table.CheckConstraint("CK_Quotations_Exempt", "NOT \"IsExempt\" OR (\"Discount\" = \"Subtotal\" AND \"DiscountReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_Quotations_Status", "\"Status\" IN ('Draft', 'Issued', 'Void')");
                    table.CheckConstraint("CK_Quotations_Subtotal", "\"Subtotal\" >= 0");
                    table.CheckConstraint("CK_Quotations_Total", "\"Total\" >= 0");
                    table.CheckConstraint("CK_Quotations_Total_Equals_Subtotal_Minus_Discount", "\"Total\" = \"Subtotal\" - \"Discount\"");
                    table.ForeignKey(
                        name: "FK_Quotations_BookingRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuotationLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuotationId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EquipmentTypeId = table.Column<long>(type: "bigint", nullable: true),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Qty = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotationLines", x => x.Id);
                    table.CheckConstraint("CK_QuotationLines_Kind", "\"Kind\" IN ('Room', 'Equipment')");
                    table.CheckConstraint("CK_QuotationLines_Kind_Type", "(\"Kind\" = 'Room') = (\"EquipmentTypeId\" IS NULL)");
                    table.CheckConstraint("CK_QuotationLines_LineTotal", "\"LineTotal\" = round(\"Qty\" * \"UnitPrice\", 2)");
                    table.CheckConstraint("CK_QuotationLines_Qty", "\"Qty\" > 0");
                    table.CheckConstraint("CK_QuotationLines_UnitPrice", "\"UnitPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_QuotationLines_EquipmentTypes_EquipmentTypeId",
                        column: x => x.EquipmentTypeId,
                        principalTable: "EquipmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuotationLines_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuotationLines_EquipmentTypeId",
                table: "QuotationLines",
                column: "EquipmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationLines_QuotationId",
                table: "QuotationLines",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_RequestId",
                table: "Quotations",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_RequestId_Live",
                table: "Quotations",
                column: "RequestId",
                unique: true,
                filter: "\"Status\" IN ('Draft', 'Issued')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuotationLines");

            migrationBuilder.DropTable(
                name: "Quotations");
        }
    }
}
