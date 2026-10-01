using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Notifications;

/// <summary>
/// Bound from the "Email" section (plan §14). BrevoApiKey comes from user-secrets (dev) or the environment
/// (Email__BrevoApiKey), never appsettings, and is never logged. Without it the no-op sender records every email as
/// Skipped, so CI and tests never call Brevo. RedirectAllTo, when set, receives every email (demo and test runs).
/// </summary>
public sealed class EmailOptions : IValidatableObject
{
    public const string SectionName = "Email";
    public const string FromAddressRequiredMessage = "Email:FromAddress is required when Email:BrevoApiKey is set.";

    public static string InvalidAddressMessage(string name) => $"Email:{name} is not a valid email address.";

    public string? BrevoApiKey { get; set; }

    /// <summary>A sender verified in Brevo. Required when a key is set.</summary>
    public string? FromAddress { get; set; }

    [Required, MaxLength(70)] public string FromName { get; set; } = "CampusSpace AI";

    public string? RedirectAllTo { get; set; }

    [Required, Url] public string BaseUrl { get; set; } = "https://api.brevo.com/v3/";

    /// <summary>Seconds between dispatcher ticks.</summary>
    [Range(1, 3600)] public int PollSeconds { get; set; } = 5;

    /// <summary>
    /// With nothing due the dispatcher makes no database query except this sweep (and wakes at once when this process
    /// queues an email), so Neon can scale to zero. Aligned to the clock, like AgentService:IdleSweepMinutes.
    /// </summary>
    [Range(1, 1440)] public int IdleSweepMinutes { get; set; } = 30;

    /// <summary>Off in Testing, where tests call NotificationDispatcher.ProcessOnceAsync themselves.</summary>
    public bool DispatcherEnabled { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BrevoApiKey);

    public string? Redirect => string.IsNullOrWhiteSpace(RedirectAllTo) ? null : RedirectAllTo.Trim();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsConfigured && string.IsNullOrWhiteSpace(FromAddress))
            yield return new ValidationResult(FromAddressRequiredMessage, [nameof(FromAddress)]);
        // Blank means "not set" (an empty .env line); anything else must be an address.
        var address = new EmailAddressAttribute();
        if (!string.IsNullOrWhiteSpace(FromAddress) && !address.IsValid(FromAddress.Trim()))
            yield return new ValidationResult(InvalidAddressMessage(nameof(FromAddress)), [nameof(FromAddress)]);
        if (Redirect is { } redirect && !address.IsValid(redirect))
            yield return new ValidationResult(InvalidAddressMessage(nameof(RedirectAllTo)), [nameof(RedirectAllTo)]);
    }
}
