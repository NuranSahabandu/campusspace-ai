using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CampusSpace.Tests.Integration;

/// <summary>The database itself enforces the agent workflow rules (§8.1), even for rows written without the API.</summary>
[Collection(PostgresCollection.Name)]
public class AgentWorkflowMigrationTests(PostgresFixture fixture)
{
    private CustomWebApplicationFactory Factory => fixture.Factory;

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task ShouldFailAsync(string sql, string sqlState, string constraint)
    {
        var act = () => ScalarAsync(sql);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.Should().Match<PostgresException>(e => e.SqlState == sqlState && e.ConstraintName == constraint);
    }

    private Task ShouldViolateCheckAsync(string sql, string constraint) =>
        ShouldFailAsync(sql, PostgresErrorCodes.CheckViolation, constraint);

    private static string InsertRun(long requestId, int revision = 1, string status = "Queued", string failureReason = "NULL",
        string duration = "NULL", string started = "NULL", string completed = "NULL") =>
        $"""INSERT INTO "AgentRuns" ("Id", "RequestId", "RevisionNo", "Status", "FailureReason", "DurationMs", "StartedAt", "CompletedAt", "CreatedAt", "UpdatedAt") VALUES ('{Guid.NewGuid()}', {requestId}, {revision}, '{status}', {failureReason}, {duration}, {started}, {completed}, now(), now())""";

    private async Task<long> RequestAsync() => (await QuotationTestData.RequestAsync(Factory)).RequestId;

    [Fact]
    public async Task Run_checks_reject_bad_revision_status_duration_times_and_a_failure_without_reason()
    {
        var requestId = await RequestAsync();

        await ShouldViolateCheckAsync(InsertRun(requestId, revision: 0), "CK_AgentRuns_RevisionNo");
        await ShouldViolateCheckAsync(InsertRun(requestId, status: "Paused"), "CK_AgentRuns_Status");
        await ShouldViolateCheckAsync(InsertRun(requestId, duration: "-1"), "CK_AgentRuns_DurationMs");
        await ShouldViolateCheckAsync(InsertRun(requestId, started: "'2030-01-01 10:00Z'", completed: "'2030-01-01 09:59Z'"),
            "CK_AgentRuns_CompletedAt_After_StartedAt");
        await ShouldViolateCheckAsync(InsertRun(requestId, status: "Failed"), "CK_AgentRuns_FailureReason");

        await ScalarAsync(InsertRun(requestId, status: "Failed", failureReason: "'policy unavailable'"));
        await ScalarAsync(InsertRun(requestId, revision: 2, duration: "0"));
        (await ScalarAsync($"""SELECT cardinality("Nodes") FROM "AgentRuns" WHERE "RequestId" = {requestId} AND "RevisionNo" = 2"""))
            .Should().Be(0);
    }

    [Fact]
    public async Task One_live_run_per_request_but_terminal_runs_do_not_count()
    {
        var requestId = await RequestAsync();
        await AgentRunTestData.InsertRunAsync(Factory, requestId, AgentRunStatuses.Running, revisionNo: 1);

        await ShouldFailAsync(InsertRun(requestId, revision: 2, status: "AwaitingApproval"),
            PostgresErrorCodes.UniqueViolation, AgentRunConfiguration.LiveRunIndex);
        await ShouldFailAsync(InsertRun(requestId, revision: 1, status: "Completed"),
            PostgresErrorCodes.UniqueViolation, "IX_AgentRuns_RequestId_RevisionNo");

        var other = await RequestAsync();
        await AgentRunTestData.InsertRunAsync(Factory, other, AgentRunStatuses.Failed, revisionNo: 1);
        await AgentRunTestData.InsertRunAsync(Factory, other, AgentRunStatuses.Rejected, revisionNo: 2);
        await AgentRunTestData.InsertRunAsync(Factory, other, AgentRunStatuses.Queued, revisionNo: 3);
        (await ScalarAsync($"""SELECT count(*) FROM "AgentRuns" WHERE "RequestId" = {other}""")).Should().Be(3L);
    }

    [Fact]
    public async Task A_cancelled_run_is_accepted_and_does_not_block_a_new_live_run()
    {
        var requestId = await RequestAsync();

        await ScalarAsync(InsertRun(requestId, revision: 1, status: AgentRunStatuses.Cancelled));
        await ScalarAsync(InsertRun(requestId, revision: 2, status: AgentRunStatuses.AwaitingApproval));

        (await ScalarAsync($"""SELECT count(*) FROM "AgentRuns" WHERE "RequestId" = {requestId}""")).Should().Be(2L);
    }

    [Fact]
    public async Task Step_tool_call_and_validation_checks_reject_bad_rows()
    {
        var runId = await AgentRunTestData.InsertRunAsync(Factory, await RequestAsync());

        string Step(int sequence = 1, string status = "Succeeded", string retries = "0", string duration = "10", string name = "venue") =>
            $"""INSERT INTO "AgentSteps" ("RunId", "Sequence", "AgentName", "Status", "Retries", "DurationMs", "CreatedAt", "UpdatedAt") VALUES ('{runId}', {sequence}, '{name}', '{status}', {retries}, {duration}, now(), now()) RETURNING "Id" """;
        await ShouldViolateCheckAsync(Step(sequence: 0), "CK_AgentSteps_Sequence");
        await ShouldViolateCheckAsync(Step(status: "Running"), "CK_AgentSteps_Status");
        await ShouldViolateCheckAsync(Step(retries: "-1"), "CK_AgentSteps_Retries");
        await ShouldViolateCheckAsync(Step(duration: "-1"), "CK_AgentSteps_DurationMs");
        await ShouldViolateCheckAsync(Step(name: " "), "CK_AgentSteps_AgentName_NotBlank");
        var stepId = (long)(await ScalarAsync(Step()))!;
        await ShouldFailAsync(Step(), PostgresErrorCodes.UniqueViolation, "IX_AgentSteps_RunId_Sequence");

        string Call(bool succeeded, string error = "NULL", string duration = "5") =>
            $"""INSERT INTO "AgentToolCalls" ("StepId", "ToolName", "Succeeded", "Error", "DurationMs", "CreatedAt", "UpdatedAt") VALUES ({stepId}, 'find_rooms', {succeeded}, {error}, {duration}, now(), now())""";
        await ShouldViolateCheckAsync(Call(false), "CK_AgentToolCalls_Error");
        await ShouldViolateCheckAsync(Call(true, duration: "-1"), "CK_AgentToolCalls_DurationMs");
        await ScalarAsync(Call(false, "'TOOL_ERROR'"));
        (await ScalarAsync($"""SELECT "ArgsJson"::text FROM "AgentToolCalls" WHERE "StepId" = {stepId}""")).Should().Be("{}");

        string Rule(string code, int attempt = 1) =>
            $"""INSERT INTO "ValidationResults" ("RunId", "Attempt", "RuleCode", "Passed", "CreatedAt", "UpdatedAt") VALUES ('{runId}', {attempt}, '{code}', true, now(), now())""";
        foreach (var bad in new[] { "V00", "V13", "X01", "V1", "v01" })
            await ShouldViolateCheckAsync(Rule(bad), "CK_ValidationResults_RuleCode");
        await ShouldViolateCheckAsync(Rule("V01", attempt: 0), "CK_ValidationResults_Attempt");
        await ScalarAsync(Rule("V01"));
        await ScalarAsync(Rule("V12"));
        await ShouldFailAsync(Rule("V01"), PostgresErrorCodes.UniqueViolation, "IX_ValidationResults_RunId_Attempt_RuleCode");
        await ScalarAsync(Rule("V01", attempt: 2));
    }

    [Fact]
    public async Task Decision_checks_need_a_known_decision_and_a_comment_to_reject_or_revise()
    {
        var requestId = await RequestAsync();
        var runId = await AgentRunTestData.InsertRunAsync(Factory, requestId, AgentRunStatuses.AwaitingApproval);
        var (_, officerId) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);

        string Decision(string decision, string comment = "NULL") =>
            $"""INSERT INTO "ApprovalDecisions" ("RequestId", "AgentRunId", "OfficerId", "Decision", "Comment", "DecidedAt", "CreatedAt", "UpdatedAt") VALUES ({requestId}, '{runId}', {officerId}, '{decision}', {comment}, now(), now(), now())""";
        await ShouldViolateCheckAsync(Decision("Maybe", "'x'"), "CK_ApprovalDecisions_Decision");
        await ShouldViolateCheckAsync(Decision("Reject"), "CK_ApprovalDecisions_Comment");
        await ShouldViolateCheckAsync(Decision("Revise", "'   '"), "CK_ApprovalDecisions_Comment");
        await ScalarAsync(Decision("Revise", "'use a lab in the new building'"));
        await ScalarAsync(Decision("Reject", "'not an academic use'"));
        await ScalarAsync(Decision("Approve"));
    }

    [Fact]
    public async Task Deleting_a_run_cascades_its_trace_but_is_blocked_by_decisions_and_quotations()
    {
        var requestId = await RequestAsync();
        var runId = await AgentRunTestData.InsertRunAsync(Factory, requestId, AgentRunStatuses.Completed);
        var stepId = await AgentRunTestData.InsertStepAsync(Factory, runId);
        await AgentRunTestData.InsertToolCallAsync(Factory, stepId);
        await AgentRunTestData.InsertValidationResultAsync(Factory, runId);

        await ScalarAsync($"""DELETE FROM "AgentRuns" WHERE "Id" = '{runId}'""");
        (await ScalarAsync($"""SELECT count(*) FROM "AgentSteps" WHERE "RunId" = '{runId}'""")).Should().Be(0L);
        (await ScalarAsync($"""SELECT count(*) FROM "AgentToolCalls" WHERE "StepId" = {stepId}""")).Should().Be(0L);
        (await ScalarAsync($"""SELECT count(*) FROM "ValidationResults" WHERE "RunId" = '{runId}'""")).Should().Be(0L);

        var decided = await AgentRunTestData.InsertRunAsync(Factory, requestId, AgentRunStatuses.Completed, revisionNo: 2);
        var (_, officerId) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);
        await AgentRunTestData.InsertDecisionAsync(Factory, requestId, decided, officerId);
        await ShouldFailAsync($"""DELETE FROM "AgentRuns" WHERE "Id" = '{decided}'""",
            PostgresErrorCodes.ForeignKeyViolation, "FK_ApprovalDecisions_AgentRuns_AgentRunId");

        var quoted = await AgentRunTestData.InsertRunAsync(Factory, requestId, AgentRunStatuses.Completed, revisionNo: 3);
        var quoteId = await QuotationTestData.CreateDraftAsync(Factory, requestId, QuotationTestData.Quote());
        await ScalarAsync($"""UPDATE "Quotations" SET "AgentRunId" = '{quoted}' WHERE "Id" = {quoteId}""");
        await ShouldFailAsync($"""DELETE FROM "AgentRuns" WHERE "Id" = '{quoted}'""",
            PostgresErrorCodes.ForeignKeyViolation, "FK_Quotations_AgentRuns_AgentRunId");
        await ShouldFailAsync($"""UPDATE "Quotations" SET "AgentRunId" = '{Guid.NewGuid()}' WHERE "Id" = {quoteId}""",
            PostgresErrorCodes.ForeignKeyViolation, "FK_Quotations_AgentRuns_AgentRunId");
    }

    [Theory]
    [InlineData("FK_AgentRuns_BookingRequests_RequestId", "RESTRICT")]
    [InlineData("FK_AgentSteps_AgentRuns_RunId", "CASCADE")]
    [InlineData("FK_AgentToolCalls_AgentSteps_StepId", "CASCADE")]
    [InlineData("FK_ValidationResults_AgentRuns_RunId", "CASCADE")]
    [InlineData("FK_ApprovalDecisions_BookingRequests_RequestId", "RESTRICT")]
    [InlineData("FK_ApprovalDecisions_AgentRuns_AgentRunId", "RESTRICT")]
    [InlineData("FK_ApprovalDecisions_Users_OfficerId", "RESTRICT")]
    [InlineData("FK_Quotations_AgentRuns_AgentRunId", "RESTRICT")]
    public async Task Foreign_keys_have_the_planned_delete_rule(string constraint, string rule)
    {
        (await ScalarAsync($"""SELECT delete_rule FROM information_schema.referential_constraints WHERE constraint_name = '{constraint}'"""))
            .Should().Be(rule);
    }

    [Fact]
    public async Task Run_is_audited_but_its_append_only_children_are_not()
    {
        var runId = await AgentRunTestData.InsertRunAsync(Factory, await RequestAsync());
        var stepId = await AgentRunTestData.InsertStepAsync(Factory, runId);
        await AgentRunTestData.InsertToolCallAsync(Factory, stepId);
        await AgentRunTestData.InsertValidationResultAsync(Factory, runId);

        await using var db = PostgresFixture.CreateDbContext(fixture.ConnectionString);
        (await db.AuditLogs.CountAsync(a => a.EntityType == nameof(AgentRun) && a.EntityId == runId.ToString())).Should().Be(1);
        (await db.AuditLogs.CountAsync(a => a.EntityType == nameof(AgentStep) || a.EntityType == nameof(AgentToolCall)
            || a.EntityType == nameof(AgentValidationResult))).Should().Be(0);
    }

    [Fact]
    public async Task A_second_live_run_through_ef_maps_to_409()
    {
        var requestId = await RequestAsync();
        await AgentRunTestData.InsertRunAsync(Factory, requestId, AgentRunStatuses.Queued);
        var act = () => AgentRunTestData.InsertRunAsync(Factory, requestId, AgentRunStatuses.Queued, revisionNo: 2);

        var error = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        Api.Middleware.GlobalExceptionHandler.Map(error).Should().Be((409, AgentRunConfiguration.LiveRunMessage));
    }
}
