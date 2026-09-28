"""V01–V12: one pass and one fail case each, the policy rules under two snapshots, and the full
validate_proposal() re-query against the fake API."""

import copy
from datetime import datetime
from decimal import Decimal
from typing import Any

import pytest
from pydantic import SecretStr

from app.tools import ToolClient, build_tools, recording
from app.validation import (
    RECOVERABLE,
    RULES,
    v01_schemas,
    v02_room_free,
    v03_capacity,
    v04_features,
    v05_slot,
    v06_timing,
    v07_blackouts,
    v08_equipment,
    v09_quote,
    v10_budget,
    v11_eligibility,
    v12_ids_seen,
    validate_proposal,
)
from tests.conftest import TEST_TOOLS_KEY
from tests.fake_api import CAMPUS, END, NOW, POLICY, START, FakeCampusApi

# A second, stricter snapshot: every policy-driven rule must follow the snapshot it is given.
STRICT = copy.deepcopy(POLICY) | {
    "opening_hours": POLICY["opening_hours"] | {"fri": {"open": "09:00", "close": "15:00"}},
    "min_lead_time_hours": 72,
    "max_advance_days_student": 10,
    "max_advance_days_lecturer": 20,
    "max_duration_hours": 2,
    "max_capacity_ratio": 1.5,
    "slot_granularity_minutes": 60,
    "max_open_requests": 1,
}


def at(text: str) -> datetime:
    return datetime.fromisoformat(text)


# ---------- V01 ----------


def test_v01_passes_valid_outputs() -> None:
    venue = {"options": [], "unmet": "none free"}
    assert v01_schemas(None, {"venue_matching": venue, "equipment_allocation": None})[0]


def test_v01_fails_injected_field() -> None:
    venue = {"options": [], "unmet": None, "status": "approved"}
    passed, message = v01_schemas(None, {"venue_matching": venue})
    assert not passed
    assert "venue_matching: status: Extra inputs are not permitted" in message


# ---------- V02 / V07 ----------


def test_v02_passes_free_room() -> None:
    assert v02_room_free("A301", True, True) == (
        True,
        "Room A301 exists and is still free for the window",
    )


@pytest.mark.parametrize(
    ("code", "room_ok", "free", "expected"),
    [
        (None, False, False, "No room was proposed"),
        ("A301", False, False, "Room A301 no longer exists or is inactive"),
        ("A301", True, False, "Room A301 is no longer free for the requested window"),
    ],
)
def test_v02_fails(code, room_ok, free, expected) -> None:
    passed, message = v02_room_free(code, room_ok, free)
    assert not passed and message.startswith(expected)


def test_v07_pass_and_fail_mention_holidays() -> None:
    ok, message = v07_blackouts("A301", True)
    assert ok and "public holidays are not checked (not implemented)" in message
    bad, message = v07_blackouts("A101", False)
    assert not bad and "blackout" in message and "not implemented" in message


# ---------- V03 (two snapshots) ----------


@pytest.mark.parametrize(
    ("policy", "capacity", "attendees", "ok"),
    [
        (POLICY, 48, 45, True),
        (POLICY, 135, 45, True),
        (POLICY, 136, 45, False),
        (POLICY, 44, 45, False),
        (STRICT, 60, 45, True),
        (STRICT, 68, 45, False),  # 1.5 × 45 = 67.5
    ],
)
def test_v03_capacity_follows_the_snapshot_ratio(policy, capacity, attendees, ok) -> None:
    assert v03_capacity(capacity, attendees, policy)[0] is ok


# ---------- V04 ----------


def test_v04_pass_and_fail() -> None:
    assert v04_features("A301", ["computers", "projector"], ["projector"])[0]
    assert v04_features("A305", ["computers"], ["computers", "projector"]) == (
        False,
        "Room A305 lacks projector",
    )


# ---------- V05 (two snapshots, .NET wording) ----------


@pytest.mark.parametrize(
    ("policy", "start", "end", "message"),
    [
        (POLICY, "2026-10-18T10:00+05:30", "2026-10-18T12:00+05:30", "Start: The campus is closed on Sundays"),
        (POLICY, "2026-10-16T07:30+05:30", "2026-10-16T09:00+05:30", "Start: Opens at 08:00 on Fridays"),
        (POLICY, "2026-10-16T14:15+05:30", "2026-10-16T15:00+05:30", "Start: Must be on a 30-minute boundary"),
        (POLICY, "2026-10-17T14:00+05:30", "2026-10-17T16:30+05:30", "End: Must end by 16:00 on Saturdays"),
        (POLICY, "2026-10-16T08:00+05:30", "2026-10-16T16:30+05:30", "End: Bookings can be at most 8 hours"),
        (STRICT, "2026-10-16T08:00+05:30", "2026-10-16T10:00+05:30", "Start: Opens at 09:00 on Fridays"),
        (STRICT, "2026-10-16T09:30+05:30", "2026-10-16T11:00+05:30", "Start: Must be on a 60-minute boundary"),
        (STRICT, "2026-10-16T10:00+05:30", "2026-10-16T13:00+05:30", "End: Bookings can be at most 2 hours"),
    ],
)  # fmt: skip
def test_v05_fails_with_dotnet_messages(policy, start, end, message) -> None:
    assert v05_slot(at(start), at(end), policy) == (False, message)


@pytest.mark.parametrize("policy", [POLICY, STRICT])
def test_v05_passes_inside_either_snapshot(policy) -> None:
    passed, message = v05_slot(at("2026-10-16T10:00+05:30"), at("2026-10-16T12:00+05:30"), policy)
    assert passed and "Fri 10:00–12:00" in message


def test_v05_reads_utc_times_in_campus_time() -> None:
    # 08:30Z is 14:00 campus time on the same Friday.
    assert v05_slot(at(START), at(END), POLICY)[0]


# ---------- V06 (two snapshots, both roles) ----------


@pytest.mark.parametrize(
    ("policy", "start", "role", "ok", "message"),
    [
        (POLICY, "2026-10-16T14:00+05:30", "Student", True, None),
        (POLICY, "2026-10-02T14:00+05:30", "Student", False, "Must start at least 48 hours from now"),
        (POLICY, "2026-09-30T14:00+05:30", "Student", False, "Start must be in the future."),
        (POLICY, "2026-12-10T10:00+05:30", "Student", False, "Can be booked at most 60 days ahead"),
        (POLICY, "2026-12-10T10:00+05:30", "Lecturer", True, None),
        (POLICY, "2027-01-05T10:00+05:30", "Lecturer", False, "Can be booked at most 90 days ahead"),
        (STRICT, "2026-10-03T14:00+05:30", "Student", False, "Must start at least 72 hours from now"),
        (STRICT, "2026-10-16T14:00+05:30", "Student", False, "Can be booked at most 10 days ahead"),
        (STRICT, "2026-10-16T14:00+05:30", "Lecturer", True, None),
    ],
)  # fmt: skip
def test_v06_lead_time_and_advance_window_per_role(policy, start, role, ok, message) -> None:
    passed, text = v06_timing(at(start), role, policy, NOW)
    assert passed is ok
    if message:
        assert text == message


# ---------- V08 ----------

REQUESTED = [{"code": "MIC-WIRELESS", "quantity": 2}, {"code": "PROJ-PORTABLE", "quantity": 1}]
COVERED = {"MIC-WIRELESS": None, "PROJ-PORTABLE": "projector", "MIC-WIRED": None}
LINES = {
    "lines": [
        {"type_code": "MIC-WIRELESS", "qty": 2, "source": "portable"},
        {"type_code": "PROJ-PORTABLE", "qty": 0, "source": "room_builtin"},
    ],
    "substitutions": [],
    "unmet": [],
}


def test_v08_passes_and_ignores_builtin_lines_for_stock() -> None:
    passed, message = v08_equipment(
        REQUESTED, LINES, {"MIC-WIRELESS": 2, "PROJ-PORTABLE": 0}, COVERED, ["projector"]
    )
    assert passed, message
    assert "PROJ-PORTABLE provided by the room (projector)" in message


def test_v08_fails_when_short() -> None:
    passed, message = v08_equipment(REQUESTED, LINES, {"MIC-WIRELESS": 1}, COVERED, ["projector"])
    assert not passed and "MIC-WIRELESS: 2 needed, 1 available" in message


def test_v08_fails_when_room_lost_the_covering_feature() -> None:
    passed, message = v08_equipment(REQUESTED, LINES, {"MIC-WIRELESS": 5}, COVERED, ["ac"])
    assert not passed and "PROJ-PORTABLE: the room does not provide it (projector)" in message


def test_v08_accepts_a_substitute() -> None:
    equipment = {
        "lines": [
            {"type_code": "MIC-WIRED", "qty": 2, "source": "substitute"},
            {"type_code": "PROJ-PORTABLE", "qty": 0, "source": "room_builtin"},
        ],
        "substitutions": [
            {
                "requested_code": "MIC-WIRELESS",
                "substitute_code": "MIC-WIRED",
                "qty": 2,
                "reason": "x",
            }
        ],
    }
    assert v08_equipment(REQUESTED, equipment, {"MIC-WIRED": 8}, COVERED, ["projector"])[0]


def test_v08_fails_on_a_missing_line() -> None:
    equipment = {"lines": [], "substitutions": []}
    passed, message = v08_equipment(REQUESTED[:1], equipment, {}, COVERED, [])
    assert not passed and "MIC-WIRELESS ×2 is not in the proposal" in message


# ---------- V09 (Decimal) ----------


def quote(lines: list[str], subtotal: str, discount: str, total: str) -> dict[str, Any]:
    return {
        "lines": [
            {"kind": "Equipment", "description": "x", "qty": "1", "unit_price": t, "line_total": t}
            for t in lines
        ],
        "subtotal": subtotal,
        "discount": discount,
        "discount_reason": None,
        "exempt": False,
        "total": total,
        "currency": "LKR",
    }


def test_v09_passes_on_exact_match() -> None:
    proposed = quote(["4500.00", "1000.00"], "5500.00", "0.00", "5500.00")
    assert v09_quote(proposed, {"total": Decimal("5500.00")}) == (
        True,
        "Total LKR 5,500.00 equals the .NET recomputation",
    )


def test_v09_is_exact_where_float_is_not() -> None:
    # 0.10 + 0.20 == 0.30 in Decimal; as floats the sum is 0.30000000000000004.
    assert 0.1 + 0.2 != 0.3
    proposed = quote(["0.10", "0.20"], "0.30", "0.00", "0.30")
    assert v09_quote(proposed, {"total": Decimal("0.30")})[0]


def test_v09_fails_on_a_one_cent_difference() -> None:
    proposed = quote(["4500.00", "1000.00"], "5500.00", "0.00", "5500.00")
    passed, message = v09_quote(proposed, {"total": Decimal("5500.01")})
    assert not passed
    assert message == "Quote total LKR 5,500.00 ≠ .NET recomputed LKR 5,500.01"


def test_v09_fails_when_lines_do_not_add_up() -> None:
    proposed = quote(["4500.00", "1000.00"], "5000.00", "0.00", "5000.00")
    assert not v09_quote(proposed, {"total": Decimal("5000.00")})[0]


# ---------- V10 ----------


def test_v10_pass_and_fail() -> None:
    assert v10_budget(Decimal("5500.00"), Decimal("8000.00")) == (
        True,
        "LKR 5,500.00 ≤ budget LKR 8,000.00",
    )
    assert v10_budget(Decimal("0.00"), Decimal("0.00"))[0]  # exempt lecturer, no budget
    assert v10_budget(Decimal("5500.00"), Decimal("5499.99")) == (
        False,
        "LKR 5,500.00 is over the budget of LKR 5,499.99",
    )


# ---------- V11 (two snapshots) ----------

CLUB = {"name": "Robotics Club", "isActive": True, "requesterIsRepresentative": True}


def context(**changes: Any) -> dict[str, Any]:
    return {"requesterRole": "Student", "club": CLUB, "openRequestCount": 0} | changes


@pytest.mark.parametrize(
    ("ctx", "policy", "ok", "message"),
    [
        (context(openRequestCount=2), POLICY, True, None),
        (context(openRequestCount=3), POLICY, False, "The requester already has 3 other open requests (limit 3)"),
        (context(openRequestCount=1), STRICT, False, "The requester already has 1 other open requests (limit 1)"),
        (context(club=CLUB | {"isActive": False}), POLICY, False, "The club is not active"),
        (context(club=CLUB | {"requesterIsRepresentative": False}), POLICY, False, "The requester is not a registered club representative"),
        (context(club=None), POLICY, False, "A student booking needs a club"),
        (context(requesterRole="Lecturer", club=None), STRICT, True, None),
    ],
)  # fmt: skip
def test_v11_eligibility(ctx, policy, ok, message) -> None:
    passed, text = v11_eligibility(ctx, policy)
    assert passed is ok
    if message:
        assert text == message


# ---------- V12 ----------


def test_v12_pass_and_fail() -> None:
    assert v12_ids_seen([1, 6], ["MIC-WIRELESS"], [1, 6, 4], ["MIC-WIRELESS", "MIC-WIRED"])[0]
    assert v12_ids_seen([99], ["MIC-WIRELESS"], [1], ["MIC-WIRELESS"]) == (
        False,
        "Not returned by any tool in this run: room 99",
    )
    assert v12_ids_seen([1], ["LASER-X"], [1], ["MIC-WIRELESS"]) == (
        False,
        "Not returned by any tool in this run: equipment LASER-X",
    )


# ---------- validate_proposal: all twelve with re-queries ----------


def proposal_state(**changes: Any) -> dict[str, Any]:
    state = {
        "request": {
            "request_id": 42,
            "role": "Student",
            "attendees": 45,
            "start": START,
            "end": END,
            "required_features": ["computers", "projector"],
            "equipment": REQUESTED,
            "budget_lkr": "8000.00",
        },
        "policy": POLICY,
        "plan_model": None,
        "venue": {
            "options": [
                {
                    "room_id": 1,
                    "code": "A301",
                    "name": "Computer Lab A301",
                    "capacity": 48,
                    "building": "Main Building",
                    "features": ["ac", "computers", "projector", "whiteboard"],
                    "reason": "fits",
                }
            ],
            "unmet": None,
        },
        "equipment": LINES,
        "quote": {
            "quote": quote(["4500.00", "1000.00"], "5500.00", "0.00", "5500.00"),
            "policy_flags": [],
            "officer_summary": "ok",
            "unmet": None,
        },
        "seen_room_ids": [1, 6],
        "seen_equipment_codes": ["MIC-WIRELESS", "PROJ-PORTABLE"],
    }
    state.update(changes)
    return state


def by_rule(results: list[tuple[str, bool, str]]) -> dict[str, tuple[bool, str]]:
    return {rule: (passed, message) for rule, passed, message in results}


@pytest.fixture
def tools_for():
    def make(api: FakeCampusApi):
        return build_tools(
            ToolClient("http://api.test", SecretStr(TEST_TOOLS_KEY), transport=api.transport())
        )

    return make


def test_validate_proposal_passes_all_twelve_and_requeries(tools_for) -> None:
    api = FakeCampusApi()

    with recording() as rec:
        results = validate_proposal(proposal_state(), tools_for(api), NOW)

    assert [r[0] for r in results] == RULES
    assert all(passed for _, passed, _ in results), [r for r in results if not r[1]]
    names = [c.tool_name for c in rec.calls]
    assert names == [
        "get_room_details",
        "search_available_rooms",
        "check_equipment_availability",
        "calculate_quote",
        "get_request_context",
    ]


def test_validate_proposal_sees_a_room_booked_since_the_proposal(tools_for) -> None:
    api = FakeCampusApi(busy={"A301": [("2026-10-16T09:00:00Z", "2026-10-16T10:00:00Z")]})

    results = by_rule(validate_proposal(proposal_state(), tools_for(api), NOW))

    assert results["V02"] == (False, "Room A301 is no longer free for the requested window")
    assert not results["V07"][0]
    assert {"V02", "V07"} <= RECOVERABLE


def test_validate_proposal_catches_a_tampered_price(tools_for) -> None:
    api = FakeCampusApi()
    tampered = quote(["0.00", "1000.00"], "1000.00", "0.00", "1000.00")
    result = {"quote": tampered, "policy_flags": [], "officer_summary": "x", "unmet": None}

    results = by_rule(validate_proposal(proposal_state(quote=result), tools_for(api), NOW))

    assert results["V09"] == (False, "Quote total LKR 1,000.00 ≠ .NET recomputed LKR 5,500.00")
    assert "V09" not in RECOVERABLE
    assert api.calls_to("quote")


def test_validate_proposal_uses_the_current_open_request_count(tools_for) -> None:
    api = FakeCampusApi()
    api.requests[42]["openRequestCount"] = 3

    results = by_rule(validate_proposal(proposal_state(), tools_for(api), NOW))

    assert results["V11"][0] is False


def test_campus_constant_matches_dotnet_offset() -> None:
    assert CAMPUS.utcoffset(None).total_seconds() == 5.5 * 3600
