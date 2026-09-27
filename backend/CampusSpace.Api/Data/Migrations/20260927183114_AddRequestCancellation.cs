using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "BookingRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CancelledByOfficer",
                table: "BookingRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLateCancellation",
                table: "BookingRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingRequests_Cancellation_NotLateAndOfficer",
                table: "BookingRequests",
                sql: "NOT (\"IsLateCancellation\" AND \"CancelledByOfficer\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingRequests_CancelledAt_Status",
                table: "BookingRequests",
                sql: "\"CancelledAt\" IS NULL OR \"Status\" = 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingRequests_Cancellation_NotLateAndOfficer",
                table: "BookingRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingRequests_CancelledAt_Status",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "CancelledByOfficer",
                table: "BookingRequests");

            migrationBuilder.DropColumn(
                name: "IsLateCancellation",
                table: "BookingRequests");
        }
    }
}
