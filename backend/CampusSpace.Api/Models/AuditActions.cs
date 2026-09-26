namespace CampusSpace.Api.Models;

/// <summary>Values of AuditLogs.Action. Entity changes use the first three; the rest are events.</summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
}
