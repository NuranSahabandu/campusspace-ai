using System.Text.Json;
using CampusSpace.Api.Agents;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>The verbatim agent-service responses in Fixtures/ (see Fixtures/README.md for how they were generated).</summary>
public static class AgentFixtures
{
    public const string AwaitingStudent = "agent-awaiting-approval-student";
    public const string AwaitingLecturer = "agent-awaiting-approval-lecturer";
    public const string Failed = "agent-failed";
    public const string CompletedStudent = "agent-completed-student";
    public const string CompletedLecturer = "agent-completed-lecturer";
    public const string RevisedStudent = "agent-revised-student";
    public const string FinalizeFailed = "agent-finalize-failed";

    public static string Json(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));

    public static AgentWorkflowView View(string name) =>
        JsonSerializer.Deserialize<AgentWorkflowView>(Json(name), AgentJson.Options)!;
}
