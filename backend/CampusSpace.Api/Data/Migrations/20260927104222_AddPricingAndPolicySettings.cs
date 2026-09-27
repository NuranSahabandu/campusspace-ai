using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingAndPolicySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicySettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    ValueType = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicySettings", x => x.Key);
                    table.CheckConstraint("CK_PolicySettings_Key", "\"Key\" IN ('opening_hours', 'min_lead_time_hours', 'max_advance_days_student', 'max_advance_days_lecturer', 'max_duration_hours', 'max_capacity_ratio', 'slot_granularity_minutes', 'free_cancellation_hours', 'max_open_requests')");
                    table.CheckConstraint("CK_PolicySettings_ValueType", "\"ValueType\" IN ('int', 'decimal', 'bool', 'json')");
                    table.ForeignKey(
                        name: "FK_PolicySettings_Users_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricingRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequesterRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    IsExempt = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingRules", x => x.Id);
                    table.CheckConstraint("CK_PricingRules_Exempt_ZeroRate", "NOT \"IsExempt\" OR \"HourlyRate\" = 0");
                    table.CheckConstraint("CK_PricingRules_HourlyRate", "\"HourlyRate\" >= 0");
                    table.CheckConstraint("CK_PricingRules_RequesterRole", "\"RequesterRole\" IN ('Student', 'Lecturer')");
                    table.CheckConstraint("CK_PricingRules_RoomType", "\"RoomType\" IN ('LectureHall', 'ComputerLab', 'SeminarRoom', 'Auditorium')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicySettings_UpdatedById",
                table: "PolicySettings",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_RoomType_RequesterRole_ValidFrom",
                table: "PricingRules",
                columns: new[] { "RoomType", "RequesterRole", "ValidFrom" },
                unique: true);

            // The default booking policy (addendum A.1), so every environment has it, not only Development where the
            // seed runs. Literals on purpose: a migration is a frozen snapshot and must not read app constants that may
            // change later (PolicySettingDefaults holds the same values for the seed; a test checks they agree).
            // UpdatedAt is a fixed UTC instant so the migration is deterministic; UpdatedById is null because no user
            // made these values.
            var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "PolicySettings",
                columns: new[] { "Key", "Value", "ValueType", "Description", "UpdatedAt", "UpdatedById" },
                values: new object[,]
                {
                    { "opening_hours", "{\"mon\":{\"open\":\"08:00\",\"close\":\"20:00\"},\"tue\":{\"open\":\"08:00\",\"close\":\"20:00\"},\"wed\":{\"open\":\"08:00\",\"close\":\"20:00\"},\"thu\":{\"open\":\"08:00\",\"close\":\"20:00\"},\"fri\":{\"open\":\"08:00\",\"close\":\"20:00\"},\"sat\":{\"open\":\"08:00\",\"close\":\"16:00\"},\"sun\":null}", "json", "Opening hours per weekday in campus time (null = closed)", seededAt, null },
                    { "min_lead_time_hours", "48", "int", "Minimum hours between submitting a request and the booking start", seededAt, null },
                    { "max_advance_days_student", "60", "int", "How many days ahead a student can book", seededAt, null },
                    { "max_advance_days_lecturer", "90", "int", "How many days ahead a lecturer can book", seededAt, null },
                    { "max_duration_hours", "8", "int", "Longest booking, in hours", seededAt, null },
                    { "max_capacity_ratio", "3", "decimal", "A room may seat at most this many times the attendees", seededAt, null },
                    { "slot_granularity_minutes", "30", "int", "Booking start and end times fall on multiples of this many minutes", seededAt, null },
                    { "free_cancellation_hours", "24", "int", "Cancelling at least this many hours before the start is free; later is flagged as late", seededAt, null },
                    { "max_open_requests", "3", "int", "Most open requests a requester can have at once", seededAt, null },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicySettings");

            migrationBuilder.DropTable(
                name: "PricingRules");
        }
    }
}
