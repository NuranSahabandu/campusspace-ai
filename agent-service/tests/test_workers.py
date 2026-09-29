from typing import Any

import pytest
from pydantic import SecretStr

from app.guardrails import OPEN_TAG
from app.schemas import Plan, PlanStep
from app.tools import ToolClient, build_tools, recording
from app.workers import WORKERS, WorkerFailed, WorkerRunner, WorkerUnavailable
from app.workers.common import make_task, parse_brief
from app.workers.supervisor import (
    SupervisorError,
    build_brief,
    enforce_plan_rules,
    load_context,
    stub_planner,
)
from tests import payloads
from tests.fake_api import END, POLICY, START, FakeCampusApi
from tests.keys import TEST_TOOLS_KEY


@pytest.fixture
def api() -> FakeCampusApi:
    return FakeCampusApi()


@pytest.fixture
def tools(api: FakeCampusApi):
    return build_tools(
        ToolClient("http://api.test", SecretStr(TEST_TOOLS_KEY), transport=api.transport())
    )


def venue_task(**changes: Any) -> str:
    brief = {
        "start": START,
        "end": END,
        "attendees": 45,
        "required_features": ["computers", "projector"],
        "excluded_room_ids": [],
        "max_capacity_ratio": 3,
        "soft_preferences": [],
        "replan_reason": None,
    } | changes
    return make_task("Find rooms.", brief)


# ---------- venue ----------


def test_venue_ranks_by_capacity_then_code(tools) -> None:
    result = WorkerRunner(tools).run_worker("venue_matching", venue_task())

    assert [o["code"] for o in result["options"]] == ["A301", "N201"]
    assert result["options"][0]["reason"] == (
        "48 seats for 45 attendees; has computers, projector; Main Building"
    )
    assert result["unmet"] is None


def test_venue_drops_excluded_and_oversized_rooms(tools) -> None:
    result = WorkerRunner(tools).run_worker(
        "venue_matching", venue_task(excluded_room_ids=[1], max_capacity_ratio=1.2)
    )

    # N201 seats 60 > 1.2 × 45 = 54, and A301 is excluded.
    assert result["options"] == []
    assert result["unmet"] == "Every free room that fits was already proposed (A301)"


def test_venue_reports_the_exact_unmet_constraint(api: FakeCampusApi, tools) -> None:
    api.busy = {"A301": [(START, END)], "N201": [(START, END)]}

    result = WorkerRunner(tools).run_worker("venue_matching", venue_task())

    assert result["unmet"] == (
        "No active room with at least 45 seats and computers, projector is free "
        f"from {START} to {END}"
    )


def test_venue_turns_a_4xx_into_unmet(tools) -> None:
    task = venue_task(start="2026-10-18T10:00:00+05:30", end="2026-10-18T12:00:00+05:30")

    result = WorkerRunner(tools).run_worker("venue_matching", task)

    assert result["unmet"].endswith("Start: The campus is closed on Sundays")


def test_venue_raises_when_the_tool_is_down(api: FakeCampusApi, tools) -> None:
    api.down.add("rooms/available")

    with pytest.raises(WorkerUnavailable, match="Tool search_available_rooms unavailable"):
        WorkerRunner(tools).run_worker("venue_matching", venue_task())


def test_worker_sees_only_its_allow_listed_tools(tools, monkeypatch) -> None:
    seen: list[str] = []

    def spy(task: str, allowed) -> dict:
        seen.extend(allowed)
        return {"options": [], "unmet": "x"}

    monkeypatch.setitem(WORKERS, "venue_matching", spy)
    WorkerRunner(tools).run_worker("venue_matching", venue_task())

    assert seen == ["search_available_rooms", "get_room_details"]


# ---------- equipment ----------


def equipment_task(features: list[str], lines: list[dict]) -> str:
    brief = {"start": START, "end": END, "room_id": 1, "room_features": features, "lines": lines}
    return make_task("Allocate.", brief)


REQUESTED = [{"code": "MIC-WIRELESS", "quantity": 2}, {"code": "PROJ-PORTABLE", "quantity": 1}]


def test_equipment_drops_a_line_the_room_covers(tools) -> None:
    result = WorkerRunner(tools).run_worker(
        "equipment_allocation", equipment_task(["computers", "projector"], REQUESTED)
    )

    assert result["lines"] == [
        {"type_code": "MIC-WIRELESS", "qty": 2, "source": "portable"},
        {"type_code": "PROJ-PORTABLE", "qty": 0, "source": "room_builtin"},
    ]


def test_equipment_keeps_the_line_portable_when_the_room_lacks_the_feature(tools) -> None:
    result = WorkerRunner(tools).run_worker(
        "equipment_allocation", equipment_task(["computers"], REQUESTED)
    )

    assert {"type_code": "PROJ-PORTABLE", "qty": 1, "source": "portable"} in result["lines"]


def test_equipment_proposes_a_directional_substitute(api: FakeCampusApi, tools) -> None:
    api.reserved = {"MIC-WIRELESS": 6}  # 7 serviceable - 6 = 1 left

    result = WorkerRunner(tools).run_worker(
        "equipment_allocation", equipment_task(["projector"], REQUESTED[:1])
    )

    assert result["lines"] == [{"type_code": "MIC-WIRED", "qty": 2, "source": "substitute"}]
    assert result["substitutions"][0]["requested_code"] == "MIC-WIRELESS"


def test_equipment_reports_unmet_without_a_substitute(api: FakeCampusApi, tools) -> None:
    api.reserved = {"MIC-WIRELESS": 6, "MIC-WIRED": 7}

    result = WorkerRunner(tools).run_worker(
        "equipment_allocation", equipment_task([], REQUESTED[:1])
    )

    assert result["lines"] == []
    assert result["unmet"] == [
        "MIC-WIRELESS: 2 requested, 1 available, and no substitute has 2 available"
    ]


# ---------- policy_cost ----------


def policy_task(**changes: Any) -> str:
    brief = {
        "start": START,
        "end": END,
        "room_id": 1,
        "room_code": "A301",
        "room_capacity": 48,
        "venue_unmet": None,
        "requester_role": "Student",
        "attendees": 45,
        "required_features": ["computers", "projector"],
        "budget_lkr": "8000.00",
        "duration_hours": "3.00",
        "priced_lines": [{"code": "MIC-WIRELESS", "quantity": 2}],
        "builtin": ["PROJ-PORTABLE"],
        "substitutions": [],
        "equipment_unmet": [],
        "policy": {"max_duration_hours": 8},
    } | changes
    return make_task("Price.", brief)


def test_policy_cost_prices_the_demo_quote_and_summarises(api: FakeCampusApi, tools) -> None:
    result = WorkerRunner(tools).run_worker("policy_cost", policy_task())

    assert result["quote"]["total"] == "5500.00"
    assert result["officer_summary"] == (
        "A301 fits 45 attendees (48 seats) with computers, projector. Equipment: "
        "2 × MIC-WIRELESS; PROJ-PORTABLE provided by the room. "
        "Total LKR 5,500.00 (budget LKR 8,000.00): within budget."
    )
    assert "PROJ-PORTABLE provided by room A301 (not priced)" in result["policy_flags"]
    sent = api.calls_to("quote")[0].read()
    assert b"PROJ-PORTABLE" not in sent  # builtin lines are never priced


def test_policy_cost_without_a_room(tools) -> None:
    result = WorkerRunner(tools).run_worker(
        "policy_cost", policy_task(room_id=None, venue_unmet="No room free")
    )

    assert result["quote"] is None
    assert result["officer_summary"] == "No proposal: No room free."


# ---------- run_worker V01 ----------


def test_invalid_output_is_retried_once_then_fails(tools, monkeypatch) -> None:
    calls = 0

    def injected(task: str, allowed) -> dict:
        nonlocal calls
        calls += 1
        return {"options": [], "unmet": None, "status": "approved"}

    monkeypatch.setitem(WORKERS, "venue_matching", injected)

    with recording() as rec, pytest.raises(WorkerFailed, match="V01: venue_matching output"):
        WorkerRunner(tools).run_worker("venue_matching", venue_task())
    assert calls == 2
    assert rec.retries == 1


def test_a_retry_that_succeeds_returns_the_valid_output(tools, monkeypatch) -> None:
    outputs = [{"options": [], "price": 0}, {"options": [], "unmet": "none"}]
    monkeypatch.setitem(WORKERS, "venue_matching", lambda task, allowed: outputs.pop(0))

    with recording() as rec:
        result = WorkerRunner(tools).run_worker("venue_matching", venue_task())

    assert result == {"options": [], "unmet": "none"}
    assert rec.retries == 1


# ---------- supervisor ----------


def test_load_context_wraps_notes_and_keeps_form_fields(api: FakeCampusApi, tools) -> None:
    api.requests[42]["notes"] = payloads.EARLY_CLOSE

    ctx = load_context(tools, 42)

    request = ctx["request"]
    assert request["notes"].startswith(OPEN_TAG)
    assert request["notes"].count("requester_notes") == 2
    assert request["budget_lkr"] == "8000.00"
    assert ctx["policy"] == POLICY
    assert "PROJ-PORTABLE" in ctx["catalogs"]["equipment"]


@pytest.mark.parametrize(
    ("setup", "reason"),
    [
        (lambda api: api.down.add("policy"), "policy unavailable (unavailable: ConnectError"),
        (lambda api: api.requests.pop(42), "Booking request not found"),
        (
            lambda api: api.status_override.update({"request-context": 400}),
            "Booking request context unavailable: 400: Server error",
        ),
        (
            lambda api: api.down.add("request-context"),
            "Tool get_request_context unavailable: ConnectError",
        ),
    ],
)
def test_load_context_failures_have_clear_reasons(api: FakeCampusApi, tools, setup, reason) -> None:
    setup(api)

    with pytest.raises(SupervisorError) as exc:
        load_context(tools, 42)
    assert str(exc.value).startswith(reason)


def test_enforce_plan_rules_orders_steps_and_follows_the_form() -> None:
    llm_plan = Plan(
        required_features=["teleporter"],
        equipment=[],
        steps=[
            PlanStep(agent="policy_cost", task="price"),
            PlanStep(agent="venue_matching", task="find"),
            PlanStep(agent="venue_matching", task="again"),
        ],
    )
    form = {"required_features": ["projector"], "equipment": [{"code": "CLICKER", "quantity": 1}]}
    catalogs = {"features": ["projector"], "equipment": ["CLICKER"]}

    plan, corrections = enforce_plan_rules(llm_plan, form, catalogs)

    assert [s.agent for s in plan.steps] == [
        "venue_matching",
        "equipment_allocation",
        "policy_cost",
    ]
    assert plan.required_features == ["projector"]
    assert plan.equipment[0].type_code == "CLICKER"
    assert "feature teleporter: not on the request form, dropped" in corrections
    assert plan.planner_fallback is False


def test_enforce_plan_rules_skips_equipment_when_none_was_requested() -> None:
    form = {"required_features": [], "equipment": []}
    catalogs = {"features": [], "equipment": []}
    plan = stub_planner(form, catalogs, None)

    assert [s.agent for s in enforce_plan_rules(plan, form, catalogs)[0].steps] == [
        "venue_matching",
        "policy_cost",
    ]


def test_equipment_brief_carries_the_chosen_room_ids_not_records(api, tools) -> None:
    ctx = load_context(tools, 42)
    state = ctx | {
        "venue": {
            "options": [
                {
                    "room_id": 1,
                    "code": "A301",
                    "name": "Computer Lab A301",
                    "capacity": 48,
                    "building": "Main Building",
                    "features": ["computers", "projector"],
                    "reason": "r",
                }
            ],
            "unmet": None,
        }
    }

    brief = parse_brief(build_brief({"agent": "equipment_allocation", "task": "Go."}, state))

    assert brief == {
        "start": START,
        "end": END,
        "room_id": 1,
        "room_features": ["computers", "projector"],
        "lines": ctx["request"]["equipment"],
    }
