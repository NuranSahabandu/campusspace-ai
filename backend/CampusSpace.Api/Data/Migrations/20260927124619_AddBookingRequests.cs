using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequesterId = table.Column<long>(type: "bigint", nullable: false),
                    ClubId = table.Column<long>(type: "bigint", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Attendees = table.Column<int>(type: "integer", nullable: false),
                    RequestedStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BudgetLkr = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    RequiredFeatures = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingRequests", x => x.Id);
                    table.CheckConstraint("CK_BookingRequests_Attendees", "\"Attendees\" BETWEEN 1 AND 2000");
                    table.CheckConstraint("CK_BookingRequests_BudgetLkr", "\"BudgetLkr\" >= 0");
                    table.CheckConstraint("CK_BookingRequests_Purpose_NotBlank", "btrim(\"Purpose\") <> ''");
                    table.CheckConstraint("CK_BookingRequests_RequestedEnd_After_Start", "\"RequestedEnd\" > \"RequestedStart\"");
                    table.CheckConstraint("CK_BookingRequests_Status", "\"Status\" IN ('Submitted', 'AgentProcessing', 'PendingApproval', 'Approved', 'Completed', 'AgentFailed', 'RevisionRequested', 'Rejected', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_BookingRequests_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingRequests_Users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestedEquipmentLines",
                columns: table => new
                {
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    TypeId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestedEquipmentLines", x => new { x.RequestId, x.TypeId });
                    table.CheckConstraint("CK_RequestedEquipmentLines_Quantity", "\"Quantity\" BETWEEN 1 AND 50");
                    table.ForeignKey(
                        name: "FK_RequestedEquipmentLines_BookingRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestedEquipmentLines_EquipmentTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "EquipmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestStatusHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChangedById = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestStatusHistory", x => x.Id);
                    table.CheckConstraint("CK_RequestStatusHistory_FromStatus", "\"FromStatus\" IS NULL OR \"FromStatus\" IN ('Submitted', 'AgentProcessing', 'PendingApproval', 'Approved', 'Completed', 'AgentFailed', 'RevisionRequested', 'Rejected', 'Cancelled')");
                    table.CheckConstraint("CK_RequestStatusHistory_ToStatus", "\"ToStatus\" IN ('Submitted', 'AgentProcessing', 'PendingApproval', 'Approved', 'Completed', 'AgentFailed', 'RevisionRequested', 'Rejected', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_RequestStatusHistory_BookingRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestStatusHistory_Users_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_ClubId",
                table: "BookingRequests",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_RequesterId_Status",
                table: "BookingRequests",
                columns: new[] { "RequesterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_Status_CreatedAt",
                table: "BookingRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestedEquipmentLines_TypeId",
                table: "RequestedEquipmentLines",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestStatusHistory_ChangedById",
                table: "RequestStatusHistory",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_RequestStatusHistory_RequestId_ChangedAt",
                table: "RequestStatusHistory",
                columns: new[] { "RequestId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestedEquipmentLines");

            migrationBuilder.DropTable(
                name: "RequestStatusHistory");

            migrationBuilder.DropTable(
                name: "BookingRequests");
        }
    }
}
