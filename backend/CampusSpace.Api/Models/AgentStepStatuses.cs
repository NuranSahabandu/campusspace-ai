namespace CampusSpace.Api.Models;

/// <summary>
/// How a finished agent step ended. A step is saved once it has finished, so there is no "running" value; retries
/// before the outcome are counted in AgentStep.Retries.
/// </summary>
public static class AgentStepStatuses
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";

    public static readonly IReadOnlyList<string> All = [Succeeded, Failed];
}
