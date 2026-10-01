using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CampusSpace.Api.Notifications;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CampusSpace.Tests.Unit;

/// <summary>The real Brevo registration (base URL, api-key header, redaction, 10 s timeout) over a recording fake handler.</summary>
public class BrevoEmailSenderTests
{
    /// <summary>A sentinel: it must never appear in a log line or a stored error.</summary>
    private const string ApiKey = "xkeysib-SENTINEL-0123456789abcdef0123456789abcdef";

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(string Path, string? Key, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues(BrevoEmailSender.ApiKeyHeader, out var keys) ? keys.Single() : null,
                await request.Content!.ReadAsStringAsync(ct)));
            return await respond(request, ct);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (IEmailSender Sender, RecordingHandler Handler, CapturingLoggerProvider Logs) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        var handler = new RecordingHandler(respond);
        var logs = new CapturingLoggerProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:BrevoApiKey"] = ApiKey,
            ["Email:FromAddress"] = "bookings@campusspace.test",
            ["Email:BaseUrl"] = "https://brevo.test/v3",
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(TimeProvider.System)
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
            .AddNotifications();
        var builder = services.AddHttpClient<BrevoEmailSender>().ConfigurePrimaryHttpMessageHandler(() => handler);
        if (timeout is { } t)
            builder.ConfigureHttpClient(c => c.Timeout = t);
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<IEmailSender>(), handler, logs);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Always(Func<HttpResponseMessage> response) =>
        (_, _) => Task.FromResult(response());

    private static EmailMessage Message(EmailAttachment? attachment = null) =>
        new("nimal@student.test", "Nimal Perera", "Booking confirmed: AI Club", "<p>Hi</p>", "Hi", attachment);

    private static void ShouldNotLeakTheKey(CapturingLoggerProvider logs, EmailSendResult result)
    {
        logs.Entries.Should().NotBeEmpty();
        logs.Entries.Select(e => e.Message).Should().NotContain(m => m.Contains("SENTINEL", StringComparison.Ordinal));
        (result.Error ?? "").Should().NotContain("SENTINEL");
    }

    [Fact]
    public async Task A_201_is_Sent_with_the_message_id_the_api_key_header_and_brevos_body()
    {
        var (sender, handler, logs) = Create(Always(() => Json(HttpStatusCode.Created, """{"messageId":"<abc@smtp-relay.mailin.fr>"}""")));
        var ics = new EmailAttachment("booking.ics", Encoding.UTF8.GetBytes("BEGIN:VCALENDAR\r\n"));

        var result = await sender.SendAsync(Message(ics));

        result.Should().Be(EmailSendResult.Sent("<abc@smtp-relay.mailin.fr>"));
        var (path, key, body) = handler.Requests.Single();
        (path, key).Should().Be(("/v3/smtp/email", ApiKey));
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("sender").GetProperty("email").GetString().Should().Be("bookings@campusspace.test");
        json.GetProperty("sender").GetProperty("name").GetString().Should().Be("CampusSpace AI");
        json.GetProperty("to")[0].GetProperty("email").GetString().Should().Be("nimal@student.test");
        json.GetProperty("subject").GetString().Should().Be("Booking confirmed: AI Club");
        json.GetProperty("htmlContent").GetString().Should().Be("<p>Hi</p>");
        json.GetProperty("textContent").GetString().Should().Be("Hi");
        var attachment = json.GetProperty("attachment").EnumerateArray().Single();
        attachment.GetProperty("name").GetString().Should().Be("booking.ics");
        Convert.FromBase64String(attachment.GetProperty("content").GetString()!).Should().Equal(ics.Content);
        ShouldNotLeakTheKey(logs, result);
    }

    [Fact]
    public async Task An_email_without_an_attachment_sends_no_attachment_field()
    {
        var (sender, handler, _) = Create(Always(() => Json(HttpStatusCode.Created, """{"messageId":"m"}""")));

        await sender.SendAsync(Message());

        JsonDocument.Parse(handler.Requests.Single().Body).RootElement.TryGetProperty("attachment", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "HTTP 401 (unauthorized)")]
    [InlineData(HttpStatusCode.Forbidden, "HTTP 403 (forbidden)")]
    public async Task A_401_or_403_is_Failed_once_with_a_fixed_error_and_never_brevos_message(HttpStatusCode status, string error)
    {
        var (sender, handler, logs) = Create(Always(() =>
            Json(status, $$"""{"code":"unauthorized","message":"Key not found: {{ApiKey}}"}""")));

        var result = await sender.SendAsync(Message());

        result.Should().Be(EmailSendResult.Failed(error));
        handler.Requests.Should().ContainSingle();
        ShouldNotLeakTheKey(logs, result);
        logs.Entries.Should().Contain(e => e.Message.Contains("check Email:BrevoApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_400_is_Failed_once_with_brevos_error_code_only()
    {
        var (sender, handler, logs) = Create(Always(() =>
            Json(HttpStatusCode.BadRequest, """{"code":"invalid_parameter","message":"email is not valid in to: nimal@student.test"}""")));

        var result = await sender.SendAsync(Message());

        result.Should().Be(EmailSendResult.Failed("HTTP 400 (invalid_parameter)"));
        handler.Requests.Should().ContainSingle();
        ShouldNotLeakTheKey(logs, result);
    }

    [Theory]
    [InlineData("""{"code":"Not A Code <b>","message":"x"}""")]
    [InlineData("not json")]
    public async Task A_400_with_an_odd_body_stores_a_fixed_label(string body)
    {
        var (sender, _, _) = Create(Always(() => Json(HttpStatusCode.BadRequest, body)));

        (await sender.SendAsync(Message())).Should().Be(EmailSendResult.Failed("HTTP 400 (bad request)"));
    }

    [Fact]
    public async Task A_429_is_a_Retry_after_its_Retry_After()
    {
        var (sender, handler, _) = Create(Always(() =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, """{"code":"too_many_requests"}""");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return response;
        }));

        var result = await sender.SendAsync(Message());

        result.Should().Be(EmailSendResult.Retry("HTTP 429 (too many requests)", TimeSpan.FromSeconds(42)));
        handler.Requests.Should().ContainSingle("the sender never retries by itself; the dispatcher does");
    }

    [Fact]
    public async Task A_429_without_Retry_After_waits_the_default_and_a_huge_one_is_capped()
    {
        var (plain, _, _) = Create(Always(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        (await plain.SendAsync(Message())).RetryAfter.Should().Be(BrevoEmailSender.DefaultRetryAfter);

        var (huge, _, _) = Create(Always(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(5));
            return response;
        }));
        (await huge.SendAsync(Message())).RetryAfter.Should().Be(BrevoEmailSender.MaxRetryAfter);
    }

    [Fact]
    public async Task A_timeout_is_Failed_and_not_retried_because_it_may_have_been_sent()
    {
        var (sender, handler, logs) = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }, timeout: TimeSpan.FromMilliseconds(100));

        var result = await sender.SendAsync(Message());

        result.Should().Be(EmailSendResult.Failed(BrevoEmailSender.TimeoutMessage));
        handler.Requests.Should().ContainSingle();
        ShouldNotLeakTheKey(logs, result);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task A_failure_to_connect_is_a_Retry_because_nothing_was_sent(HttpRequestError error)
    {
        var (sender, handler, logs) = Create((_, _) =>
            throw new HttpRequestException(error, $"Connection refused (brevo.test:443) {ApiKey}"));

        var result = await sender.SendAsync(Message());

        result.Should().Be(EmailSendResult.Retry($"Connection failed ({error})"));
        handler.Requests.Should().ContainSingle();
        ShouldNotLeakTheKey(logs, result);
    }

    [Fact]
    public async Task A_failure_after_connecting_is_Failed_and_not_retried()
    {
        var (sender, _, logs) = Create((_, _) => throw new HttpRequestException(HttpRequestError.ResponseEnded, "ended"));

        var result = await sender.SendAsync(Message());

        result.Outcome.Should().Be(EmailSendOutcome.Failed);
        result.Error.Should().Be("Request failed (ResponseEnded; not retried: it may have been sent)");
        ShouldNotLeakTheKey(logs, result);
    }

    [Fact]
    public async Task A_5xx_is_Failed_and_not_retried()
    {
        var (sender, handler, _) = Create(Always(() => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        (await sender.SendAsync(Message())).Should().Be(EmailSendResult.Failed("HTTP 502 (server error; not retried: it may have been sent)"));
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public void Without_a_key_the_no_op_sender_is_used()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:BrevoApiKey"] = "",
        }).Build();
        var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .AddNotifications()
            .BuildServiceProvider();

        provider.CreateScope().ServiceProvider.GetRequiredService<IEmailSender>().Should().BeOfType<NoOpEmailSender>();
    }

    [Fact]
    public void A_key_without_a_from_address_fails_validation()
    {
        var options = new EmailOptions { BrevoApiKey = ApiKey };
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), errors, validateAllProperties: true)
            .Should().BeFalse();
        errors.Select(e => e.ErrorMessage).Should().Contain(EmailOptions.FromAddressRequiredMessage);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("  ", null, true)]
    [InlineData("demo@campusspace.test", "", true)]
    [InlineData("not-an-address", "", false)]
    [InlineData("", "nope", false)]
    public void Blank_addresses_mean_not_set_and_anything_else_must_be_an_address(string? redirect, string? from, bool valid)
    {
        var options = new EmailOptions { RedirectAllTo = redirect, FromAddress = from };
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), errors, validateAllProperties: true)
            .Should().Be(valid);
    }
}
