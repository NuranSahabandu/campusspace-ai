using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentRunId",
                table: "Quotations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    RevisionNo = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlanJson = table.Column<string>(type: "jsonb", nullable: true),
                    ProposalJson = table.Column<string>(type: "jsonb", nullable: true),
                    PolicySnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    OfficerSummary = table.Column<string>(type: "text", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Nodes = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.Id);
                    table.CheckConstraint("CK_AgentRuns_CompletedAt_After_StartedAt", "\"StartedAt\" IS NULL OR \"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"StartedAt\"");
                    table.CheckConstraint("CK_AgentRuns_DurationMs", "\"DurationMs\" IS NULL OR \"DurationMs\" >= 0");
                    table.CheckConstraint("CK_AgentRuns_FailureReason", "\"Status\" <> 'Failed' OR \"FailureReason\" IS NOT NULL");
                    table.CheckConstraint("CK_AgentRuns_RevisionNo", "\"RevisionNo\" > 0");
                    table.CheckConstraint("CK_AgentRuns_Status", "\"Status\" IN ('Queued', 'Running', 'AwaitingApproval', 'Resuming', 'Completed', 'Rejected', 'Failed')");
                    table.ForeignKey(
                        name: "FK_AgentRuns_BookingRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentSteps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    AgentName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InputJson = table.Column<string>(type: "jsonb", nullable: true),
                    OutputJson = table.Column<string>(type: "jsonb", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Error = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSteps", x => x.Id);
                    table.CheckConstraint("CK_AgentSteps_AgentName_NotBlank", "btrim(\"AgentName\") <> ''");
                    table.CheckConstraint("CK_AgentSteps_DurationMs", "\"DurationMs\" >= 0");
                    table.CheckConstraint("CK_AgentSteps_Retries", "\"Retries\" >= 0");
                    table.CheckConstraint("CK_AgentSteps_Sequence", "\"Sequence\" >= 1");
                    table.CheckConstraint("CK_AgentSteps_Status", "\"Status\" IN ('Succeeded', 'Failed')");
                    table.ForeignKey(
                        name: "FK_AgentSteps_AgentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficerId = table.Column<long>(type: "bigint", nullable: false),
                    Decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDecisions", x => x.Id);
                    table.CheckConstraint("CK_ApprovalDecisions_Comment", "\"Decision\" = 'Approve' OR (\"Comment\" IS NOT NULL AND btrim(\"Comment\") <> '')");
                    table.CheckConstraint("CK_ApprovalDecisions_Decision", "\"Decision\" IN ('Approve', 'Reject', 'Revise')");
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_AgentRuns_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_BookingRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "BookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_Users_OfficerId",
                        column: x => x.OfficerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValidationResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    RuleCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationResults", x => x.Id);
                    table.CheckConstraint("CK_ValidationResults_Attempt", "\"Attempt\" > 0");
                    table.CheckConstraint("CK_ValidationResults_RuleCode", "\"RuleCode\" ~ '^V(0[1-9]|1[0-2])$'");
                    table.ForeignKey(
                        name: "FK_ValidationResults_AgentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentToolCalls",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StepId = table.Column<long>(type: "bigint", nullable: false),
                    ToolName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ArgsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    ResultSummary = table.Column<string>(type: "jsonb", nullable: true),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentToolCalls", x => x.Id);
                    table.CheckConstraint("CK_AgentToolCalls_DurationMs", "\"DurationMs\" >= 0");
                    table.CheckConstraint("CK_AgentToolCalls_Error", "\"Succeeded\" OR \"Error\" IS NOT NULL");
                    table.CheckConstraint("CK_AgentToolCalls_ToolName_NotBlank", "btrim(\"ToolName\") <> ''");
                    table.ForeignKey(
                        name: "FK_AgentToolCalls_AgentSteps_StepId",
                        column: x => x.StepId,
                        principalTable: "AgentSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_AgentRunId",
                table: "Quotations",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_RequestId_Live",
                table: "AgentRuns",
                column: "RequestId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Running', 'AwaitingApproval', 'Resuming')");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_RequestId_RevisionNo",
                table: "AgentRuns",
                columns: new[] { "RequestId", "RevisionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_Status_CreatedAt",
                table: "AgentRuns",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSteps_RunId_Sequence",
                table: "AgentSteps",
                columns: new[] { "RunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentToolCalls_StepId",
                table: "AgentToolCalls",
                column: "StepId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_AgentRunId",
                table: "ApprovalDecisions",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_OfficerId",
                table: "ApprovalDecisions",
                column: "OfficerId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_RequestId",
                table: "ApprovalDecisions",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationResults_RunId_Attempt_RuleCode",
                table: "ValidationResults",
                columns: new[] { "RunId", "Attempt", "RuleCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_AgentRuns_AgentRunId",
                table: "Quotations",
                column: "AgentRunId",
                principalTable: "AgentRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_AgentRuns_AgentRunId",
                table: "Quotations");

            migrationBuilder.DropTable(
                name: "AgentToolCalls");

            migrationBuilder.DropTable(
                name: "ApprovalDecisions");

            migrationBuilder.DropTable(
                name: "ValidationResults");

            migrationBuilder.DropTable(
                name: "AgentSteps");

            migrationBuilder.DropTable(
                name: "AgentRuns");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_AgentRunId",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "AgentRunId",
                table: "Quotations");
        }
    }
}
