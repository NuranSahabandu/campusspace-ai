using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Notifications;

/// <summary>
/// Brevo's transactional email API (POST smtp/email). The typed HttpClient is registered in NotificationServiceExtensions
/// with the base URL, the api-key header and the 10 s timeout; IHttpClientFactory's header logging is redacted. Each call
/// logs status and duration only: never the key, the body, the recipient or a provider message.
///
/// Retry decision (plan §6 "never auto-retry writes" vs §14 "2 retries for transient errors"): this POST is retried only
/// when Brevo can't have accepted the email, so a retry can't send it twice. That is a 429 (Brevo refused it; honour
/// Retry-After) or a failure to connect at all (DNS, TCP or TLS: nothing was sent). A timeout or a 5xx may come after Brevo
/// accepted it, and Brevo has no idempotency key, so those are Failed and never retried. The dispatcher makes the retry
/// later (at most 3 attempts in all); this class sends once.
/// </summary>
public sealed partial class BrevoEmailSender(
    HttpClient http, IOptions<EmailOptions> options, TimeProvider clock, ILogger<BrevoEmailSender> logger) : IEmailSender
{
    public const string ApiKeyHeader = "api-key";
    public const string Route = "smtp/email";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(15);
    public const string TimeoutMessage = "Timed out after 10 s (not retried: it may have been sent)";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        var body = new BrevoEmail(
            new BrevoAddress(o.FromAddress!.Trim(), o.FromName),
            [new BrevoAddress(message.To, message.ToName)],
            message.Subject, message.Html, message.Text,
            message.Attachment is { } a ? [new BrevoAttachment(a.Name, Convert.ToBase64String(a.Content))] : null);

        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(Route, body, Json, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Brevo POST {Route} timed out after {ElapsedMs} ms", Route, Elapsed(started));
            return EmailSendResult.Failed(TimeoutMessage);
        }
        catch (HttpRequestException ex)
        {
            // Only the error kind: never the exception message.
            logger.LogWarning("Brevo POST {Route} failed: {ErrorType} after {ElapsedMs} ms", Route, ex.HttpRequestError, Elapsed(started));
            return ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError
                or HttpRequestError.SecureConnectionError
                ? EmailSendResult.Retry($"Connection failed ({ex.HttpRequestError})")
                : EmailSendResult.Failed($"Request failed ({ex.HttpRequestError}; not retried: it may have been sent)");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            logger.LogInformation("Brevo POST {Route} returned {StatusCode} in {ElapsedMs} ms", Route, status, Elapsed(started));
            if (response.IsSuccessStatusCode)
                return EmailSendResult.Sent(await MessageIdAsync(response, ct));

            switch (response.StatusCode)
            {
                case HttpStatusCode.TooManyRequests:
                    return EmailSendResult.Retry("HTTP 429 (too many requests)", RetryAfter(response));
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    logger.LogError("Brevo rejected the email: HTTP {StatusCode}, check Email:BrevoApiKey", status);
                    return EmailSendResult.Failed(StatusMessage(response.StatusCode));
                case HttpStatusCode.BadRequest:
                    return EmailSendResult.Failed($"HTTP 400 ({await ErrorCodeAsync(response, ct) ?? "bad request"})");
                case >= HttpStatusCode.InternalServerError:
                    return EmailSendResult.Failed($"HTTP {status} (server error; not retried: it may have been sent)");
                default:
                    return EmailSendResult.Failed(StatusMessage(response.StatusCode));
            }
        }
    }

    /// <summary>The fixed label stored for a status: the status and its name, never anything Brevo sent.</summary>
    public static string StatusMessage(HttpStatusCode status) => (int)status switch
    {
        401 => "HTTP 401 (unauthorized)",
        403 => "HTTP 403 (forbidden)",
        404 => "HTTP 404 (not found)",
        var code => string.Create(CultureInfo.InvariantCulture, $"HTTP {code}"),
    };

    private TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var after = header?.Delta ?? (header?.Date is { } date ? date - clock.GetUtcNow() : DefaultRetryAfter);
        return after < TimeSpan.Zero ? TimeSpan.Zero : after > MaxRetryAfter ? MaxRetryAfter : after;
    }

    /// <summary>Brevo's error "code" (for example invalid_parameter) when it is a plain identifier; never its "message".</summary>
    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<BrevoError>(Json, ct);
            return error?.Code is { } code && ErrorCodePattern().IsMatch(code) ? code : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> MessageIdAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var sent = await response.Content.ReadFromJsonAsync<BrevoSent>(Json, ct);
            return sent?.MessageId is { Length: > 0 and <= 100 } id ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    [GeneratedRegex("^[a-z_]{1,40}$")]
    private static partial Regex ErrorCodePattern();

    private sealed record BrevoAddress(string Email, string Name);

    private sealed record BrevoAttachment(string Name, string Content);

    private sealed record BrevoEmail(
        BrevoAddress Sender, IReadOnlyList<BrevoAddress> To, string Subject, string HtmlContent, string TextContent,
        IReadOnlyList<BrevoAttachment>? Attachment);

    private sealed record BrevoSent(string? MessageId);

    private sealed record BrevoError(string? Code);
}
