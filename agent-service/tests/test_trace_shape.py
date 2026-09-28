"""The trace maps 1:1 onto the 3.1 tables and satisfies their CHECK constraints
(AgentStepConfiguration, AgentToolCallConfiguration, AgentValidationResultConfiguration)."""

import re
from collections.abc import Iterator
from pathlib import Path

import pytest

from app.schemas import WorkflowView
from tests.fake_api import FakeCampusApi
from tests.harness import Harness

STEP_STATUSES = {"Succeeded", "Failed"}  # AgentStepStatuses.All
RULE_CODE = re.compile(r"^V(0[1-9]|1[0-2])$")  # CK_ValidationResults_RuleCode
STEP_KEYS = {
    "sequence",
    "agent_name",
    "status",
    "input",
    "output",
    "retries",
    "error",
    "duration_ms",
    "tool_calls",
}
TOOL_KEYS = {"tool_name", "args", "result_summary", "succeeded", "error", "duration_ms"}


@pytest.fixture
def api() -> FakeCampusApi:
    return FakeCampusApi()


@pytest.fixture
def h(tmp_path: Path, api: FakeCampusApi) -> Iterator[Harness]:
    harness = Harness(tmp_path / "cp.sqlite", api)
    yield harness
    harness.close()


def assert_trace_fits_the_tables(view: WorkflowView) -> None:
    sequences = [s["sequence"] for s in view.steps]
    assert sequences == list(range(1, len(view.steps) + 1))  # Sequence >= 1, unique per run
    for step in view.steps:
        assert set(step) == STEP_KEYS
        assert step["status"] in STEP_STATUSES
        assert step["agent_name"].strip() and len(step["agent_name"]) <= 50
        assert step["retries"] >= 0 and step["duration_ms"] >= 0
        if step["status"] == "Failed":
            assert step["error"]
        for call in step["tool_calls"]:
            assert set(call) == TOOL_KEYS
            assert call["tool_name"].strip() and len(call["tool_name"]) <= 50
            assert call["duration_ms"] >= 0
            assert isinstance(call["args"], dict)  # ArgsJson jsonb
            assert call["result_summary"] is None or isinstance(call["result_summary"], dict)
            assert call["succeeded"] or call["error"]  # CK_AgentToolCalls_Error
    pairs = set()
    for row in view.validation:
        assert set(row) == {"attempt", "rule", "passed", "message"}
        assert row["attempt"] > 0  # CK_ValidationResults_Attempt
        assert RULE_CODE.match(row["rule"])
        pairs.add((row["attempt"], row["rule"]))
    assert len(pairs) == len(view.validation)
    if view.status == "failed":
        assert view.error  # CK_AgentRuns_FailureReason
    assert len(view.model) <= 100


def test_completed_run_trace_fits_the_tables(h: Harness) -> None:
    tid = h.start()
    h.resume(tid, "revise", "Try another room.")

    view = h.resume(tid, "approve")

    assert view.status == "completed"
    assert_trace_fits_the_tables(view)


@pytest.mark.parametrize("down", [{"rooms/available"}, {"request-context"}, {"*"}])
def test_failed_run_trace_fits_the_tables(h: Harness, api: FakeCampusApi, down) -> None:
    api.down = down

    view = h.view(h.start())

    assert view.status == "failed"
    assert_trace_fits_the_tables(view)
    failed_calls = [c for s in view.steps for c in s["tool_calls"] if not c["succeeded"]]
    assert failed_calls and all(c["error"] for c in failed_calls)


def test_each_tool_call_sits_under_the_step_that_made_it(h: Harness) -> None:
    tid = h.start()
    view = h.resume(tid, "approve")

    by_agent = {}
    for step in view.steps:
        by_agent.setdefault(step["agent_name"], []).append(
            [c["tool_name"] for c in step["tool_calls"]]
        )

    assert by_agent["supervisor"] == [
        ["get_policy", "get_request_context", "list_feature_catalog", "list_equipment_catalog"]
    ]
    assert by_agent["venue_matching"] == [["search_available_rooms"]]
    assert by_agent["equipment_allocation"] == [["check_equipment_availability"]]
    assert by_agent["policy_cost"] == [["calculate_quote"]]
    assert by_agent["validate"] == [
        [
            "get_room_details",
            "search_available_rooms",
            "check_equipment_availability",
            "calculate_quote",
            "get_request_context",
        ]
    ]
    assert by_agent["finalize"] == [
        ["get_room_details", "search_available_rooms", "check_equipment_availability"]
    ]
    assert [s["agent_name"] for s in view.steps] == [
        "supervisor",
        "venue_matching",
        "equipment_allocation",
        "policy_cost",
        "validate",
        "finalize",
    ]


def test_trace_holds_summaries_never_keys_or_raw_notes(h: Harness) -> None:
    from tests.keys import TEST_SERVICE_KEY, TEST_TOOLS_KEY

    tid = h.start()
    view = h.view(tid)
    text = view.model_dump_json() + h.state_json(tid)

    assert TEST_TOOLS_KEY not in text and TEST_SERVICE_KEY not in text
    assert "unlock the room 15 minutes early" in h.state(tid)["request"]["notes"]
    assert "unlock the room 15 minutes early" not in view.model_dump_json()
    context_call = view.steps[0]["tool_calls"][1]
    assert context_call["result_summary"]["notes"] == "<omitted>"
