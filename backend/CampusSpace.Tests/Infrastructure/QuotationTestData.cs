using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Quotation rows for tests. Each test submits a request of its own (a new student rep) and saves quotes with the real
/// IQuotationService, inside a transaction as the Phase 3 poller will.
/// </summary>
public static class QuotationTestData
{
    /// <summary>A new student rep and a request they submitted through the API.</summary>
    public static async Task<(HttpClient Client, long UserId, long RequestId)> RequestAsync(CustomWebApplicationFactory factory)
    {
        var (client, userId, clubId) = await BookingRequestTestData.StudentRepAsync(factory);
        var response = await client.PostAsJsonAsync(BookingRequestTestData.Url, BookingRequestTestData.Body(clubId));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (client, userId, (await response.ReadJsonAsync()).GetProperty("id").GetInt64());
    }

    /// <summary>A room line of 3 h at 1,500, plus 2 of <paramref name="typeId"/> at <paramref name="fee"/> when given.</summary>
    public static QuoteResult Quote(long? typeId = null, decimal fee = 500m, bool exempt = false)
    {
        List<QuoteLine> lines = [QuotationCalculator.Line(QuotationLineKinds.Room, null, "Computer lab T1, 3 h @ LKR 1,500", 3m, 1500m)];
        if (typeId is { } id)
            lines.Add(QuotationCalculator.Line(QuotationLineKinds.Equipment, id, $"Test type x2 @ LKR {fee}", 2m, fee));
        return Result(lines, exempt);
    }

    public static QuoteResult Result(IReadOnlyList<QuoteLine> lines, bool exempt = false)
    {
        var subtotal = lines.Sum(l => l.LineTotal);
        var discount = exempt ? subtotal : 0m;
        return new QuoteResult(lines, subtotal, discount, exempt ? QuotationCalculator.LecturerExemptionReason : null, exempt, subtotal - discount);
    }

    /// <summary>Saves <paramref name="quote"/> as the request's Draft and commits. Returns the new quotation's id.</summary>
    public static async Task<long> CreateDraftAsync(CustomWebApplicationFactory factory, long requestId, QuoteResult quote)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var quotation = await service.CreateDraftAsync(requestId, quote);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return quotation.Id;
    }
}
