using System.Text.Json;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CampusSpace.Tests.Unit;

public class GlobalExceptionHandlerTests
{
    private static async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection().AddLogging().AddErrorHandling().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "test-trace-id" };
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, json.RootElement.Clone());
    }

    private static PostgresException Postgres(string sqlState, string? constraintName = null) =>
        new("secret internal detail", "ERROR", "ERROR", sqlState, constraintName: constraintName);

    [Fact]
    public async Task ForbiddenException_maps_to_403_with_its_message()
    {
        var (status, body) = await HandleAsync(new ForbiddenException("You can't view this request"));

        status.Should().Be(StatusCodes.Status403Forbidden);
        body.GetProperty("status").GetInt32().Should().Be(403);
        body.GetProperty("title").GetString().Should().Be("You can't view this request");
        body.GetProperty("traceId").GetString().Should().Be("test-trace-id");
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "Duplicate value")]
    [InlineData(PostgresErrorCodes.ExclusionViolation, "Time slot was just booked")]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "In use")]
    public async Task Postgres_conflicts_wrapped_by_ef_map_to_409(string sqlState, string title)
    {
        var (status, body) = await HandleAsync(new DbUpdateException("save failed", Postgres(sqlState)));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("status").GetInt32().Should().Be(409);
        body.GetProperty("title").GetString().Should().Be(title);
        body.GetProperty("traceId").GetString().Should().Be("test-trace-id");
    }

    [Theory]
    [InlineData(ClubMemberConfiguration.OneRepresentativeIndex, "Club already has a representative")]
    [InlineData(PricingRuleConfiguration.UniqueRuleIndex, "A rule for this room type, role and date already exists")]
    [InlineData(QuotationConfiguration.LiveQuoteIndex, "Request already has a live quotation")]
    [InlineData("IX_Users_Email", "Duplicate value")]
    [InlineData("AK_Features_Code", "Duplicate value")]
    public async Task Unique_violations_are_mapped_by_constraint_name(string constraintName, string title)
    {
        var exception = new DbUpdateException("save failed", Postgres(PostgresErrorCodes.UniqueViolation, constraintName));

        var (status, body) = await HandleAsync(exception);

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("title").GetString().Should().Be(title);
    }

    [Fact]
    public async Task A_double_booking_on_no_room_overlap_maps_to_409_time_slot_was_just_booked()
    {
        var exception = new DbUpdateException("save failed",
            Postgres(PostgresErrorCodes.ExclusionViolation, BookingConfiguration.NoRoomOverlapConstraint));

        var (status, body) = await HandleAsync(exception);

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("title").GetString().Should().Be("Time slot was just booked");
    }

    [Fact]
    public async Task ConflictException_maps_to_409_with_its_message_as_title()
    {
        var (status, body) = await HandleAsync(new ConflictException("Email is already registered"));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("title").GetString().Should().Be("Email is already registered");
        body.GetProperty("traceId").GetString().Should().Be("test-trace-id");
    }

    [Fact]
    public async Task BusinessRuleException_maps_to_400_with_a_field_error()
    {
        var (status, body) = await HandleAsync(new BusinessRuleException("IsActive", "You cannot deactivate your own account."));

        status.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("title").GetString().Should().Be("You cannot deactivate your own account.");
        body.GetProperty("errors").GetProperty("IsActive")[0].GetString()
            .Should().Be("You cannot deactivate your own account.");
        body.GetProperty("traceId").GetString().Should().Be("test-trace-id");
    }

    [Fact]
    public async Task BusinessRuleException_with_several_fields_returns_every_error()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["opening_hours"] = ["sat: opening time must be before closing time.", "At least one day must be open."],
            ["max_duration_hours"] = ["Must be between 1 and 24."],
        };

        var (status, body) = await HandleAsync(new BusinessRuleException(errors));

        status.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("title").GetString().Should().Be("sat: opening time must be before closing time.");
        body.GetProperty("errors").GetProperty("opening_hours").GetArrayLength().Should().Be(2);
        body.GetProperty("errors").GetProperty("max_duration_hours")[0].GetString().Should().Be("Must be between 1 and 24.");
    }

    [Fact]
    public async Task Other_exceptions_map_to_500_without_leaking_details()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("secret internal detail"));

        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("title").GetString().Should().Be("An unexpected error occurred");
        body.GetProperty("traceId").GetString().Should().Be("test-trace-id");
        body.TryGetProperty("detail", out _).Should().BeFalse();
        body.TryGetProperty("exception", out _).Should().BeFalse();
        body.GetRawText().Should().NotContain("secret internal detail");
    }
}
