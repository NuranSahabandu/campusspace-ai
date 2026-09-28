using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusSpace.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRunCancelled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentRuns_Status",
                table: "AgentRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentRuns_Status",
                table: "AgentRuns",
                sql: "\"Status\" IN ('Queued', 'Running', 'AwaitingApproval', 'Resuming', 'Completed', 'Rejected', 'Failed', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AgentRuns_Status",
                table: "AgentRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgentRuns_Status",
                table: "AgentRuns",
                sql: "\"Status\" IN ('Queued', 'Running', 'AwaitingApproval', 'Resuming', 'Completed', 'Rejected', 'Failed')");
        }
    }
}
