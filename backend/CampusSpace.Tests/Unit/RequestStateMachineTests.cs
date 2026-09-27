using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

public class RequestStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 4, 30, 0, TimeSpan.Zero);
    private readonly RequestStateMachine _machine = new(new FixedTimeProvider(Now));

    /// <summary>The §8.3 table, written out independently of the implementation.</summary>
    private static readonly HashSet<(string From, string To)> Allowed =
    [
        (RequestStatuses.Submitted, RequestStatuses.AgentProcessing),
        (RequestStatuses.Submitted, RequestStatuses.Cancelled),
        (RequestStatuses.AgentProcessing, RequestStatuses.PendingApproval),
        (RequestStatuses.AgentProcessing, RequestStatuses.AgentFailed),
        (RequestStatuses.PendingApproval, RequestStatuses.Approved),
        (RequestStatuses.PendingApproval, RequestStatuses.Rejected),
        (RequestStatuses.PendingApproval, RequestStatuses.RevisionRequested),
        (RequestStatuses.PendingApproval, RequestStatuses.Cancelled),
        (RequestStatuses.RevisionRequested, RequestStatuses.AgentProcessing),
        (RequestStatuses.AgentFailed, RequestStatuses.AgentProcessing),
        (RequestStatuses.Approved, RequestStatuses.Completed),
        (RequestStatuses.Approved, RequestStatuses.Cancelled),
    ];

    public static TheoryData<string, string> AllPairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in RequestStatuses.All)
            foreach (var to in RequestStatuses.All)
                data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_pair_of_the_9x9_matrix_is_allowed_or_forbidden_exactly_as_in_the_table(string from, string to)
    {
        var request = new BookingRequest { Status = from };
        var act = () => _machine.Transition(request, to, changedById: 7, reason: "r");

        if (Allowed.Contains((from, to)))
        {
            act.Should().NotThrow();
            request.Status.Should().Be(to);
            RequestStateMachine.CanTransition(from, to).Should().BeTrue();
        }
        else
        {
            act.Should().Throw<ConflictException>().WithMessage($"Can't move a request from {from} to {to}");
            request.Status.Should().Be(from);
            request.StatusHistory.Should().BeEmpty();
            RequestStateMachine.CanTransition(from, to).Should().BeFalse();
        }
    }

    [Fact]
    public void The_table_covers_every_status_and_nothing_else()
    {
        RequestStateMachine.AllowedTransitions.Keys.Should().BeEquivalentTo(RequestStatuses.All);
        RequestStateMachine.AllowedTransitions.SelectMany(t => t.Value.Select(to => (t.Key, to)))
            .Should().BeEquivalentTo(Allowed);
    }

    [Fact]
    public void Terminal_states_have_no_exits()
    {
        foreach (var terminal in RequestStatuses.Terminal)
            RequestStateMachine.AllowedTransitions[terminal].Should().BeEmpty(terminal);
        RequestStatuses.Terminal.Should().BeEquivalentTo([RequestStatuses.Completed, RequestStatuses.Rejected, RequestStatuses.Cancelled]);
    }

    [Fact]
    public void A_transition_adds_exactly_one_history_row_with_from_to_by_reason_and_time()
    {
        var request = new BookingRequest { Status = RequestStatuses.PendingApproval };

        _machine.Transition(request, RequestStatuses.Rejected, changedById: 42, reason: "Room is under renovation");

        request.StatusHistory.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            FromStatus = RequestStatuses.PendingApproval,
            ToStatus = RequestStatuses.Rejected,
            ChangedById = (long?)42,
            Reason = "Room is under renovation",
            ChangedAt = Now.UtcDateTime,
        });
        request.StatusHistory[0].ChangedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Start_writes_null_to_Submitted_and_a_system_change_has_no_user()
    {
        var request = new BookingRequest();

        _machine.Start(request, changedById: 5);
        _machine.Transition(request, RequestStatuses.AgentProcessing, changedById: null);

        request.Status.Should().Be(RequestStatuses.AgentProcessing);
        request.StatusHistory.Select(h => (h.FromStatus, h.ToStatus, h.ChangedById)).Should().Equal(
            (null, RequestStatuses.Submitted, 5L),
            (RequestStatuses.Submitted, RequestStatuses.AgentProcessing, null));
    }
}
