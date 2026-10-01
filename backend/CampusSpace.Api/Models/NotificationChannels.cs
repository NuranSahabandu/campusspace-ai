namespace CampusSpace.Api.Models;

/// <summary>The only list of notification channels (plan §8 NotificationLogs.Channel). Flutter's local notifications aren't one.</summary>
public static class NotificationChannels
{
    public const string Email = "Email";

    public static readonly IReadOnlyList<string> All = [Email];
}
