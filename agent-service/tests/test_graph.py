"""End-to-end graph scenarios over the fake .NET API (no network, no LLM)."""

import json
from collections import Counter
from collections.abc import Iterator
from decimal import Decimal
from pathlib import Path

import pytest

import app.graph
from app.guardrails import CLOSE_TAG, OPEN_TAG
from app.limits import GRAPH_RECURSION_LIMIT, MAX_DELEGATIONS, MAX_REPLANS
from app.validation import RULES
from tests import payloads
from tests.fake_api import FakeCampusApi, student_request
from tests.harness import HAPPY_NODES, Harness, latest

NO_EQUIPMENT_NODES = [
    "supervisor",
    "venue_matching",
    "supervisor",
    "policy_cost",
    "supervisor",
    "validate",
    "human_gate",
]


@pytest.fixture
def api() -> FakeCampusApi:
    return FakeCampusApi()


@pytest.fixture
def h(tmp_path: Path, api: FakeCampusApi) -> Iterator[Harness]:
    harness = Harness(tmp_path / "cp.sqlite", api)
    yield harness
    harness.close()


def projector_only(**changes) -> dict:
    """45 attendees, projector only, no equipment: A301, N201, A102, E101, A101 all fit."""
    return student_request(requiredFeatures=["projector"], equipment=[], **changes)


# ---------- happy path ----------


def test_happy_path_pauses_with_all_twelve_rules_passed(h: Harness) -> None:
    tid = h.start()
    view = h.view(tid)

    assert view.status == "awaiting_approval"
    assert view.nodes == HAPPY_NODES
    assert [(v["rule"], v["passed"], v["attempt"]) for v in view.validation] == [
        (rule, True, 1) for rule in RULES
    ]
    assert view.proposal["room_code"] == "A301"
    assert view.proposal["quote"]["total"] == "5500.00"
    assert view.revision == 1
    assert view.model == "stub"
    assert view.policy_snapshot["max_open_requests"] == 3
    assert view.error is None and view.completed_at is None


def test_interrupt_payload_matches_appendix_b(h: Harness) -> None:
    view = h.view(h.start())
    payload = view.interrupt

    assert payload["request_id"] == 42
    assert [o["code"] for o in payload["proposal"]["venue"]["options"]] == ["A301", "N201"]
    assert payload["quote"]["total"] == "5500.00"
    assert [v["rule"] for v in payload["validation"]] == RULES
    assert payload["summary"].startswith("A301 fits 45 attendees (48 seats)")


def test_portable_projector_is_room_builtin_when_the_room_has_one(h: Harness) -> None:
    lines = h.view(h.start()).proposal["equipment"]["lines"]

    assert lines == [
        {"type_code": "MIC-WIRELESS", "qty": 2, "source": "portable"},
        {"type_code": "PROJ-PORTABLE", "qty": 0, "source": "room_builtin"},
    ]


def test_no_equipment_skips_equipment_allocation(h: Harness, api: FakeCampusApi) -> None:
    api.requests[42] = student_request(equipment=[])

    view = h.view(h.start())

    assert view.status == "awaiting_approval"
    assert view.nodes == NO_EQUIPMENT_NODES
    assert view.proposal["equipment"] is None
    assert view.proposal["quote"]["total"] == "4500.00"


def test_short_equipment_gets_a_substitute(h: Harness, api: FakeCampusApi) -> None:
    api.reserved = {"MIC-WIRELESS": 6}

    view = h.view(h.start())

    assert view.status == "awaiting_approval"
    assert {"type_code": "MIC-WIRED", "qty": 2, "source": "substitute"} in view.proposal[
        "equipment"
    ]["lines"]
    assert view.proposal["quote"]["total"] == "4900.00"  # 4,500 + 2 × 200


# ---------- decisions ----------


def test_approve_completes_with_human_gate_recorded_once(h: Harness) -> None:
    tid = h.start()

    view = h.resume(tid, "approve")

    assert view.status == "completed"
    assert view.nodes == HAPPY_NODES + ["finalize"]
    assert view.nodes.count("human_gate") == 1
    assert view.proposal["room_id"] == 1 and view.proposal["finalized_at"]
    assert view.completed_at and view.duration_ms is not None
    assert view.interrupt is None


def test_approve_fails_when_the_room_was_booked_meanwhile(h: Harness, api: FakeCampusApi) -> None:
    tid = h.start()
    api.busy = {"A301": [("2026-10-16T09:00:00Z", "2026-10-16T10:00:00Z")]}

    view = h.resume(tid, "approve")

    assert view.status == "failed"
    assert view.error == (
        "Final re-check failed: V02: Room A301 is no longer free for the requested window"
    )


def test_reject_ends_the_run(h: Harness) -> None:
    tid = h.start()

    view = h.resume(tid, "reject")

    assert view.status == "rejected"
    assert view.nodes == HAPPY_NODES + ["rejected"]


def test_cancel_ends_the_run_and_nothing_else(h: Harness, api: FakeCampusApi) -> None:
    tid = h.start()
    calls_before = len(api.calls)

    view = h.resume(tid, "cancel")

    assert view.status == "cancelled"
    assert view.nodes == HAPPY_NODES + ["cancelled"]
    assert len(api.calls) == calls_before  # no tool call, no side effect


def test_revise_gives_a_new_room_in_the_same_thread(h: Harness) -> None:
    tid = h.start()

    view = h.resume(tid, "revise", "Use a lab in the New Building; A301 has AC maintenance.")

    assert view.status == "awaiting_approval"
    assert view.revision == 2
    assert view.proposal["room_code"] == "N201"
    assert Counter(v["attempt"] for v in view.validation) == {1: 12, 2: 12}
    assert all(v["passed"] for v in latest(view))
    assert view.nodes == HAPPY_NODES + HAPPY_NODES  # human_gate persisted once + shown once
    assert view.interrupt["revision"] == 2


def test_revise_twice_keeps_every_trace_number_unique(h: Harness, api: FakeCampusApi) -> None:
    api.requests[42] = projector_only()
    tid = h.start()
    h.resume(tid, "revise", "Not A301.")

    view = h.resume(tid, "revise", "Not N201 either.")

    assert view.status == "awaiting_approval"
    assert view.revision == 3
    assert view.proposal["room_code"] == "A102"
    sequences = [s["sequence"] for s in view.steps]
    assert sequences == list(range(1, len(sequences) + 1))
    pairs = [(v["attempt"], v["rule"]) for v in view.validation]
    assert len(pairs) == len(set(pairs)) == 36
    assert view.nodes.count("human_gate") == 3


# ---------- re-plans and failures ----------


def test_over_budget_replans_to_a_cheaper_room(h: Harness, api: FakeCampusApi) -> None:
    api.requests[42] = projector_only(budgetLkr=Decimal("4000.00"))
    api.busy = {"N201": [("2026-10-16T08:30:00Z", "2026-10-16T11:30:00Z")]}

    view = h.view(h.start())

    assert view.status == "awaiting_approval"
    first = [v for v in view.validation if v["attempt"] == 1]
    assert [v["rule"] for v in first if not v["passed"]] == ["V10"]
    assert view.proposal["room_code"] == "A102"  # lecture hall, 3 h × 1,000
    assert view.proposal["quote"]["total"] == "3000.00"


def test_two_replans_with_equipment_fit_the_delegation_cap_and_recursion_limit(
    h: Harness, api: FakeCampusApi
) -> None:
    api.requests[42] = student_request(
        requiredFeatures=["projector"],
        equipment=[{"code": "MIC-WIRELESS", "quantity": 1}],
        budgetLkr=Decimal("4000.00"),
    )

    view = h.view(h.start())

    # A301 and N201 cost 5,000; A102 costs 3,500: attempt 3 passes after exactly MAX_REPLANS.
    assert view.status == "awaiting_approval"
    assert max(v["attempt"] for v in view.validation) == MAX_REPLANS + 1
    workers = [n for n in view.nodes if n not in ("supervisor", "validate", "human_gate")]
    assert len(workers) == MAX_DELEGATIONS == 9
    assert len(view.nodes) == 25 < GRAPH_RECURSION_LIMIT


def test_revise_then_two_replans_stays_under_the_recursion_limit(
    h: Harness, api: FakeCampusApi
) -> None:
    api.requests[42] = student_request(
        requiredFeatures=["projector"], equipment=[{"code": "MIC-WIRELESS", "quantity": 1}]
    )
    tid = h.start()
    first = h.view(tid)
    assert first.proposal["room_code"] == "A301"
    api.reserved = {"MIC-WIRELESS": 7, "MIC-WIRED": 8}  # every microphone gone: V08 fails anywhere

    view = h.resume(tid, "revise", "Somewhere else, please.")

    # The worst single invocation: human_gate + 3 × 8 (N201, A102, E101) + safe_failure = 26.
    resumed = view.nodes[len(first.nodes) - 1 :]
    assert resumed[0] == "human_gate" and resumed[-1] == "safe_failure"
    assert len(resumed) == 26 < GRAPH_RECURSION_LIMIT
    assert view.status == "failed"
    assert view.error.startswith("Validation still failing after 2 re-plans: V08:")
    assert max(v["attempt"] for v in view.validation) == 4


def test_sunday_is_a_v05_safe_failure(h: Harness, api: FakeCampusApi) -> None:
    api.requests[42] = student_request(
        requestedStart="2026-10-18T04:30:00Z", requestedEnd="2026-10-18T07:30:00Z"
    )

    view = h.view(h.start())

    assert view.status == "failed"
    assert view.error.startswith("Validation failed: V05: Start: The campus is closed on Sundays")
    assert view.nodes[-2:] == ["validate", "safe_failure"]
    assert view.proposal is None


def test_lecturer_advance_window_comes_from_the_policy(h: Harness, api: FakeCampusApi) -> None:
    # 2026-12-10 is 70 days ahead: outside the student window (60), inside the lecturer's (90).
    far = {"requestedStart": "2026-12-10T08:30:00Z", "requestedEnd": "2026-12-10T11:30:00Z"}
    api.requests[43].update(far)
    api.requests[42].update(far)

    lecturer = h.view(h.start(43))
    student = h.view(h.start(42))
    api.policy["max_advance_days_lecturer"] = 60
    lecturer_tight = h.view(h.start(43))

    assert lecturer.status == "awaiting_approval"
    assert lecturer.proposal["quote"]["exempt"] is True
    assert lecturer.proposal["quote"]["total"] == "0.00"
    assert student.error == "Validation failed: V06: Can be booked at most 60 days ahead"
    assert lecturer_tight.error == "Validation failed: V06: Can be booked at most 60 days ahead"


def test_lead_time_comes_from_the_policy(h: Harness, api: FakeCampusApi) -> None:
    api.policy["min_lead_time_hours"] = 400  # the request starts in 365.5 hours

    view = h.view(h.start())

    assert view.error == "Validation failed: V06: Must start at least 400 hours from now"


@pytest.mark.parametrize(
    ("down", "reason"),
    [
        ({"*"}, "policy unavailable (unavailable: ConnectError: Connection refused)"),
        (
            {"request-context"},
            "Tool get_request_context unavailable: ConnectError: Connection refused",
        ),
    ],
)
def test_tool_down_at_start_is_a_safe_failure(h: Harness, api: FakeCampusApi, down, reason) -> None:
    api.down = down

    view = h.view(h.start())

    assert view.status == "failed"
    assert view.error == reason
    assert view.nodes == ["supervisor", "safe_failure"]
    assert view.steps[0]["status"] == "Failed"


def test_tool_down_mid_run_fails_the_step_with_a_reason(h: Harness, api: FakeCampusApi) -> None:
    api.down = {"rooms/available"}

    view = h.view(h.start())

    assert view.status == "failed"
    assert view.error == (
        "venue_matching failed: Tool search_available_rooms unavailable: "
        "ConnectError: Connection refused"
    )
    assert view.nodes == ["supervisor", "venue_matching", "supervisor", "safe_failure"]
    venue_step = view.steps[1]
    assert venue_step["status"] == "Failed"
    assert venue_step["tool_calls"][0]["succeeded"] is False
    assert venue_step["tool_calls"][0]["error"].startswith("unavailable:")


@pytest.mark.parametrize(
    ("setup", "reason"),
    [
        (lambda api: api.requests.clear(), "Booking request not found"),
        (
            lambda api: api.status_override.update({"request-context": 400}),
            "Booking request context unavailable: 400: Server error",
        ),
    ],
)
def test_request_context_4xx_is_a_safe_failure(
    h: Harness, api: FakeCampusApi, setup, reason
) -> None:
    setup(api)

    view = h.view(h.start())

    assert (view.status, view.error) == ("failed", reason)
    assert view.nodes == ["supervisor", "safe_failure"]


def test_delegation_cap_stops_a_runaway(h: Harness, monkeypatch: pytest.MonkeyPatch) -> None:
    # Inject a routing bug: the step index never advances, so venue_matching is re-dispatched.
    monkeypatch.setattr(app.graph, "advance_step", lambda index: index)

    view = h.view(h.start())

    assert view.status == "failed"
    assert view.error == "Delegation cap reached: 9 worker calls in revision 1"
    assert view.nodes.count("venue_matching") == MAX_DELEGATIONS
    assert view.nodes[-1] == "safe_failure"


def test_run_timeout_is_a_recorded_failure(tmp_path: Path) -> None:
    ticks = iter(range(0, 10_000, 100))
    harness = Harness(tmp_path / "t.sqlite", run_timeout_s=250, monotonic=lambda: next(ticks))
    try:
        view = harness.view(harness.start())
    finally:
        harness.close()

    assert view.status == "failed"
    assert view.error == "Run timed out"
    assert view.nodes[-1] == "safe_failure"


def test_restart_mid_run_reports_failed(h: Harness) -> None:
    config = {"configurable": {"thread_id": "abandoned"}}
    from app.graph import initial_state
    from tests.fake_api import NOW

    for _ in h.graph.stream(initial_state(42, NOW), config, stream_mode="updates"):
        break  # the process "dies" after the first node

    view = h.view("abandoned")

    assert view.status == "failed"
    assert view.error == "Agent service restarted during the run"


# ---------- prompt injection ----------


@pytest.mark.parametrize("payload", payloads.ALL)
def test_injection_notes_change_nothing_and_stay_wrapped(
    h: Harness, api: FakeCampusApi, payload: str
) -> None:
    api.requests[42]["notes"] = payload

    tid = h.start()
    view = h.view(tid)

    assert view.status == "awaiting_approval"
    assert view.proposal["room_code"] == "A301"
    assert view.proposal["quote"]["total"] == "5500.00"
    notes = h.state(tid)["request"]["notes"]
    assert notes.startswith(OPEN_TAG + "\n") and notes.endswith("\n" + CLOSE_TAG)
    assert notes.count("requester_notes") == 2  # tags inside the payload were stripped
    marker = payload.replace("</requester_notes>", "").replace("<requester_notes>", "").strip()
    marker = marker.splitlines()[0] if marker.splitlines()[0] else marker.splitlines()[1]
    state_text = h.state_json(tid)
    assert state_text.count(json.dumps(marker)[1:-1]) == 1  # only the wrapped copy in state
    assert marker not in view.model_dump_json()  # never in the trace or the view
