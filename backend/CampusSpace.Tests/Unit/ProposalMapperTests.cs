using CampusSpace.Api.Agents;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampusSpace.Tests.Unit;

/// <summary>The chosen room is the proposal's room_id, whatever the order of venue.options (Phase 4 LLM workers may reorder).</summary>
public class ProposalMapperTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    private static string Option(long id, string code) =>
        $$"""{"room_id":{{id}},"code":"{{code}}","name":"Room {{code}}","capacity":50,"building":"Main","features":["projector"],"reason":"Reason {{code}}"}""";

    private static string Proposal(long roomId, params string[] options) => $$"""
        {"revision":1,"room_id":{{roomId}},"room_code":"X{{roomId}}","room_name":"Agent name {{roomId}}",
         "venue":{"options":[{{string.Join(",", options)}}],"unmet":null},
         "equipment":{"lines":[{"type_code":"MIC-WIRELESS","qty":2,"source":"portable"},
                               {"type_code":"PROJ-PORTABLE","qty":0,"source":"room_builtin"},
                               {"type_code":"SPK-PORTABLE","qty":1,"source":"substitute"}],
                      "substitutions":[{"requested_code":"SPK-BIG","substitute_code":"SPK-PORTABLE","qty":1,"reason":"Short"}],
                      "unmet":["Laser pointer"]},
         "quote":{"lines":[],"subtotal":"0.00","discount":"0.00","discount_reason":null,"exempt":false,"total":"0.00","currency":"LKR"},
         "policy_flags":["Within budget"],"officer_summary":"S"}
        """;

    [Fact]
    public void Chosen_room_is_the_option_matching_room_id_even_when_it_is_not_first()
    {
        var json = Proposal(6, Option(1, "A301"), Option(6, "N201"), Option(7, "N202"));

        var dto = ProposalMapper.Map(json, new Dictionary<long, ProposalRoomRef>(), NullLogger.Instance, RunId)!;

        (dto.Chosen.RoomId, dto.Chosen.Code, dto.Chosen.Reason).Should().Be((6L, "N201", "Reason N201"));
        dto.Alternatives.Select(a => a.Code).Should().Equal("A301", "N202");
        dto.EquipmentLines.Select(l => l.Source).Should().Equal("portable", "room_builtin", "substitute");
        dto.Substitutions.Should().ContainSingle(s => s.SubstituteCode == "SPK-PORTABLE" && s.Reason == "Short");
        dto.EquipmentUnmet.Should().Equal("Laser pointer");
        dto.PolicyFlags.Should().Equal("Within budget");
    }

    [Fact]
    public void At_most_two_alternatives_when_the_chosen_room_is_listed()
    {
        var json = Proposal(1, Option(1, "A301"), Option(6, "N201"), Option(7, "N202"));

        var dto = ProposalMapper.Map(json, new Dictionary<long, ProposalRoomRef>(), NullLogger.Instance, RunId)!;

        dto.Chosen.Code.Should().Be("A301");
        dto.Alternatives.Should().HaveCount(2);
    }

    [Fact]
    public void Unlisted_chosen_room_comes_from_Rooms_every_option_is_an_alternative_and_a_warning_is_logged()
    {
        var json = Proposal(9, Option(1, "A301"), Option(6, "N201"), Option(7, "N202"));
        var rooms = new Dictionary<long, ProposalRoomRef> { [9] = new(9, "B105", "Seminar B105", 40, "Main Building") };
        using var provider = new CapturingLoggerProvider();

        var dto = ProposalMapper.Map(json, rooms, provider.CreateLogger("test"), RunId)!;

        dto.Chosen.Should().BeEquivalentTo(new { RoomId = 9L, Code = "B105", Name = "Seminar B105", Capacity = 40, Building = "Main Building", Reason = (string?)null });
        dto.Alternatives.Select(a => a.Code).Should().Equal("A301", "N201", "N202");
        provider.Entries.Should().ContainSingle(e => e.Message.Contains("room 9 is not among its venue options"));
    }

    [Fact]
    public void Malformed_proposal_gives_null_and_a_warning()
    {
        using var provider = new CapturingLoggerProvider();

        ProposalMapper.Map("{\"room_id\": \"not a number\"", new Dictionary<long, ProposalRoomRef>(), provider.CreateLogger("test"), RunId)
            .Should().BeNull();
        provider.Entries.Should().ContainSingle(e => e.Message.Contains("can't be read"));
        ProposalMapper.Map(null, new Dictionary<long, ProposalRoomRef>(), NullLogger.Instance, RunId).Should().BeNull();
    }

    [Fact]
    public void RoomId_reads_room_id_or_null()
    {
        ProposalMapper.RoomId(Proposal(6, Option(6, "N201"))).Should().Be(6);
        ProposalMapper.RoomId(null).Should().BeNull();
        ProposalMapper.RoomId("[1]").Should().BeNull();
        ProposalMapper.RoomId("{").Should().BeNull();
    }
}
