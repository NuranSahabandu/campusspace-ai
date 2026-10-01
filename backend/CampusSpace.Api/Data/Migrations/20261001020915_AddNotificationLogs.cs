using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    StatusHistoryId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RedirectedTo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                    table.CheckConstraint("CK_NotificationLogs_Attempts", "\"Attempts\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_NotificationLogs_Channel", "\"Channel\" IN ('Email')");
                    table.CheckConstraint("CK_NotificationLogs_Error", "\"Status\" <> 'Failed' OR \"Error\" IS NOT NULL");
                    table.CheckConstraint("CK_NotificationLogs_Kind", "\"Kind\" IN ('Approved', 'Rejected', 'Closed', 'RevisionRequested', 'CancelledByOfficer')");
                    table.CheckConstraint("CK_NotificationLogs_SentAt", "(\"Status\" = 'Sent') = (\"SentAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_NotificationLogs_Status", "\"Status\" IN ('Pending', 'Sending', 'Sent', 'Failed', 'Skipped')");
                    table.ForeignKey(
                        name: "FK_NotificationLogs_BookingRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_RequestStatusHistory_StatusHistoryId",
                        column: x => x.StatusHistoryId,
                        principalTable: "RequestStatusHistory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_LastAttemptAt",
                table: "NotificationLogs",
                column: "LastAttemptAt",
                filter: "\"Status\" = 'Sending'");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_NextAttemptAt",
                table: "NotificationLogs",
                column: "NextAttemptAt",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_RequestId_CreatedAt",
                table: "NotificationLogs",
                columns: new[] { "RequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_StatusHistoryId",
                table: "NotificationLogs",
                column: "StatusHistoryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationLogs");
        }
    }
}
