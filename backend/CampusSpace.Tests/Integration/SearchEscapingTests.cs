using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.SearchCases;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// Every searchable list endpoint matches %, _ and \ literally (QueryableExtensions.WhereContains): each term returns
/// exactly its own row, and never the decoy the character would match as a LIKE wildcard or escape (see SearchCases).
/// GET /api/agent-runs is covered in AgentRunsMonitorTests (its own database); pricing rules below.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SearchEscapingTests(PostgresFixture fixture)
{
    private CustomWebApplicationFactory Factory => fixture.Factory;

    [Theory]
    [InlineData("rooms")]
    [InlineData("rooms/availability")]
    [InlineData("users")]
    [InlineData("clubs")]
    [InlineData("equipment-types")]
    [InlineData("equipment-items")]
    [InlineData("loans")]
    [InlineData("booking-requests (requester, purpose)")]
    [InlineData("booking-requests (officer, purpose)")]
    [InlineData("booking-requests (officer, club name)")]
    [InlineData("approvals/queue")]
    [InlineData("audit-logs")]
    public async Task Search_matches_percent_underscore_and_backslash_literally(string target)
    {
        var marker = Marker();
        var rows = Rows(marker);
        Func<string, Task<IReadOnlyList<string>>> search = target switch
        {
            "rooms" => await RoomsAsync(rows),
            "rooms/availability" => await AvailabilityAsync(rows),
            "users" => await UsersAsync(rows),
            "clubs" => await ClubsAsync(rows),
            "equipment-types" => await EquipmentTypesAsync(rows),
            "equipment-items" => await EquipmentItemsAsync(rows),
            "loans" => await LoansAsync(rows),
            "booking-requests (requester, purpose)" => await RequesterRequestsAsync(rows),
            "booking-requests (officer, purpose)" => await OfficerRequestsAsync(rows, byClub: false),
            "booking-requests (officer, club name)" => await OfficerRequestsAsync(rows, byClub: true),
            "approvals/queue" => await ApprovalQueueAsync(rows),
            "audit-logs" => await AuditLogsAsync(rows),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        await AssertExactAsync(marker, search, target);
    }

    /// <summary>
    /// Room types and roles are fixed values (CHECK constraints), so no rule can contain %, _ or \: only the negative
    /// side can be shown here. As wildcards, "_" and "%" would match every rule and "Lectur_Hall" LectureHall.
    /// </summary>
    [Theory]
    [InlineData("_", false)]
    [InlineData("%", false)]
    [InlineData(@"\", false)]
    [InlineData("Lectur_Hall", false)]
    [InlineData("Lectur%Hall", false)]
    [InlineData("  lecturehall ", true)]
    public async Task Pricing_rules_search_takes_wildcards_literally(string term, bool matches)
    {
        // The shared database is not seeded: these rules are what a wildcard would wrongly match.
        await PricingTestData.InsertAsync(Factory, RoomTypes.LectureHall, Roles.Student, PricingTestData.UniqueFutureDate());
        await PricingTestData.InsertAsync(Factory, RoomTypes.ComputerLab, Roles.Lecturer, PricingTestData.UniqueFutureDate());
        var officer = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);
        var body = await (await officer.GetAsync($"/api/pricing-rules?pageSize=100&search={Q(term)}")).ReadJsonAsync();

        var types = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("roomType").GetString()).ToList();
        if (matches)
            types.Should().NotBeEmpty().And.OnlyContain(t => t == RoomTypes.LectureHall);
        else
            types.Should().BeEmpty();
    }

    // ---------- seeding: each returns term → the searched column of every returned row ----------

    private async Task<Func<string, Task<IReadOnlyList<string>>>> RoomsAsync(IReadOnlyList<string> names)
    {
        await InsertRoomsAsync(names);
        var client = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);
        return term => ListAsync(client, $"/api/rooms?pageSize=100&search={Q(term)}", "name");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> AvailabilityAsync(IReadOnlyList<string> names)
    {
        await InsertRoomsAsync(names);
        var client = TestAuth.CreateClient(Factory, Roles.Student);
        var start = BookingRequestTestData.FutureStart(25);
        var window = $"start={Q(start.ToString("O"))}&end={Q(start.AddHours(2).ToString("O"))}&minCapacity=1";
        return term => ListAsync(client, $"/api/rooms/availability?{window}&pageSize=100&search={Q(term)}", "name");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> UsersAsync(IReadOnlyList<string> names)
    {
        // Through the public register endpoint, as real users arrive.
        var anonymous = Factory.CreateClient();
        foreach (var fullName in names)
            (await anonymous.PostAsJsonAsync("/api/auth/register",
                new { fullName, email = $"{Guid.NewGuid():N}@campus.test", password = "correct-horse-1" }))
                .EnsureSuccessStatusCode();
        var admin = TestAuth.CreateClient(Factory, Roles.Admin);
        return term => ListAsync(admin, $"/api/users?pageSize=100&search={Q(term)}", "fullName");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> ClubsAsync(IReadOnlyList<string> names)
    {
        await InsertClubsAsync(names);
        var client = TestAuth.CreateClient(Factory, Roles.Student);
        return term => ListAsync(client, $"/api/clubs?pageSize=100&search={Q(term)}", "name");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> EquipmentTypesAsync(IReadOnlyList<string> names)
    {
        // Codes can't hold % or \ (CK_EquipmentTypes_Code_Format), so the names carry the cases.
        foreach (var name in names)
            await WithDbAsync(db =>
            {
                db.EquipmentTypes.Add(new EquipmentType
                {
                    Code = EquipmentTestData.UniqueTypeCode(), Name = name, Category = EquipmentCategories.Audio, FeePerBooking = 100m,
                });
                return db.SaveChangesAsync();
            });
        var client = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);
        return term => ListAsync(client, $"/api/equipment-types?pageSize=100&search={Q(term)}", "name");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> EquipmentItemsAsync(IReadOnlyList<string> tags)
    {
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        foreach (var tag in tags)
            await EquipmentTestData.CreateItemAsync(Factory, type.Id, assetTag: tag);
        var client = TestAuth.CreateClient(Factory, Roles.LabTechnician);
        return term => ListAsync(client, $"/api/equipment-items?pageSize=100&search={Q(term)}", "assetTag");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> LoansAsync(IReadOnlyList<string> tags)
    {
        var start = BookingRequestTestData.FutureStart(26);
        var handover = await LoanTestData.HandoverAsync(Factory, start, start.AddHours(2), reserved: tags.Count);
        var (technician, technicianId) = await LoanTestData.TechnicianAsync(Factory);
        foreach (var (itemId, tag) in handover.ItemIds.Zip(tags))
        {
            await WithDbAsync(db => db.EquipmentItems.Where(i => i.Id == itemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.AssetTag, tag)));
            await LoanTestData.InsertLoanAsync(Factory, handover.BookingId, itemId, technicianId, start.AddMinutes(-10), start.AddHours(2));
        }
        return term => ListAsync(technician, $"/api/loans?pageSize=100&search={Q(term)}", "assetTag");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> RequesterRequestsAsync(IReadOnlyList<string> purposes)
    {
        var (student, studentId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        await InsertRequestsAsync(studentId, purposes);
        return term => ListAsync(student, $"/api/booking-requests?pageSize=100&search={Q(term)}", "purpose");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> OfficerRequestsAsync(IReadOnlyList<string> texts, bool byClub)
    {
        var (_, studentId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        if (!byClub)
            await InsertRequestsAsync(studentId, texts);
        else
        {
            // Neutral purposes; the club names carry the cases.
            var clubIds = await InsertClubsAsync(texts);
            var requestIds = await InsertRequestsAsync(studentId, texts.Select(_ => $"Club event {Guid.NewGuid():N}").ToList());
            foreach (var (requestId, clubId) in requestIds.Zip(clubIds))
                await WithDbAsync(db => db.BookingRequests.Where(r => r.Id == requestId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.ClubId, clubId)));
        }
        var officer = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);
        return term => ListAsync(officer, $"/api/booking-requests?pageSize=100&search={Q(term)}", byClub ? "clubName" : "purpose");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> ApprovalQueueAsync(IReadOnlyList<string> purposes)
    {
        var (_, studentId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        foreach (var requestId in await InsertRequestsAsync(studentId, purposes))
            await BookingRequestTestData.MoveAsync(Factory, requestId, RequestStatuses.AgentProcessing, RequestStatuses.PendingApproval);
        var officer = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);
        return term => ListAsync(officer, $"/api/approvals/queue?pageSize=100&search={Q(term)}", "purpose");
    }

    private async Task<Func<string, Task<IReadOnlyList<string>>>> AuditLogsAsync(IReadOnlyList<string> userNames)
    {
        // The search covers the acting user's name: one audit row per user, as IAuditService writes them.
        foreach (var fullName in userNames)
            await WithDbAsync(async db =>
            {
                var user = new User { FullName = fullName, Email = $"{Guid.NewGuid():N}@campus.test", PasswordHash = "x", Role = Roles.Student };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                db.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "Test", EntityType = "SearchTest", At = DateTime.UtcNow });
                await db.SaveChangesAsync();
            });
        var admin = TestAuth.CreateClient(Factory, Roles.Admin);
        return term => ListAsync(admin, $"/api/audit-logs?pageSize=100&entityType=SearchTest&search={Q(term)}", "userName");
    }

    // ---------- helpers ----------

    private static async Task<IReadOnlyList<string>> ListAsync(HttpClient client, string url, string field)
    {
        var response = await client.GetAsync(url);
        response.IsSuccessStatusCode.Should().BeTrue($"GET {url} answered {(int)response.StatusCode}");
        var body = await response.ReadJsonAsync();
        body.GetProperty("total").GetInt32().Should().BeLessThanOrEqualTo(100, "every match must fit on one page");
        return body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty(field).ValueKind == JsonValueKind.Null ? "" : i.GetProperty(field).GetString()!)
            .ToList();
    }

    private async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task InsertRoomsAsync(IReadOnlyList<string> names)
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(Factory, prefix);
        await WithDbAsync(db =>
        {
            // Codes stay neutral so only the names carry the cases.
            db.Rooms.AddRange(names.Select((name, i) => new Room
            {
                BuildingId = buildingId, Code = $"{prefix}-{i}", Name = name, Type = RoomTypes.SeminarRoom, Capacity = 30,
            }));
            return db.SaveChangesAsync();
        });
    }

    private async Task<List<long>> InsertClubsAsync(IReadOnlyList<string> names)
    {
        var clubs = names.Select(name => new Club { Name = name }).ToList();
        await WithDbAsync(db =>
        {
            db.Clubs.AddRange(clubs);
            return db.SaveChangesAsync();
        });
        return clubs.Select(c => c.Id).ToList();
    }

    private async Task<List<long>> InsertRequestsAsync(long requesterId, IReadOnlyList<string> purposes)
    {
        var ids = new List<long>();
        for (var i = 0; i < purposes.Count; i++)
        {
            var id = await BookingRequestTestData.InsertSubmittedAsync(Factory, requesterId, BookingRequestTestData.FutureStart(10 + i));
            var purpose = purposes[i];
            await WithDbAsync(db => db.BookingRequests.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Purpose, purpose)));
            ids.Add(id);
        }
        return ids;
    }
}
