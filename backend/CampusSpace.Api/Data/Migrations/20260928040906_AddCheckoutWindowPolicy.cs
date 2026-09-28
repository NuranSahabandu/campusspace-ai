using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutWindowPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicySettings_Key",
                table: "PolicySettings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicySettings_Key",
                table: "PolicySettings",
                sql: "\"Key\" IN ('opening_hours', 'min_lead_time_hours', 'max_advance_days_student', 'max_advance_days_lecturer', 'max_duration_hours', 'max_capacity_ratio', 'slot_granularity_minutes', 'free_cancellation_hours', 'max_open_requests', 'checkout_window_minutes')");

            // The checkout window (plan §9 Component B, addendum Open question 5 decided as a policy key). Literals on
            // purpose, like AddPricingAndPolicySettings: a migration is frozen and must not read PolicySettingDefaults.
            migrationBuilder.InsertData(
                table: "PolicySettings",
                columns: new[] { "Key", "Value", "ValueType", "Description", "UpdatedAt", "UpdatedById" },
                values: new object[]
                {
                    "checkout_window_minutes", "30", "int",
                    "How many minutes before a booking's start a technician may hand equipment over",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null,
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PolicySettings",
                keyColumn: "Key",
                keyValue: "checkout_window_minutes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicySettings_Key",
                table: "PolicySettings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicySettings_Key",
                table: "PolicySettings",
                sql: "\"Key\" IN ('opening_hours', 'min_lead_time_hours', 'max_advance_days_student', 'max_advance_days_lecturer', 'max_duration_hours', 'max_capacity_ratio', 'slot_granularity_minutes', 'free_cancellation_hours', 'max_open_requests')");
        }
    }
}
