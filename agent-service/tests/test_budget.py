"""The shared per-segment LLM budget (app/budget.py) and its arithmetic against RUN_TIMEOUT_S."""

import json
import re
from pathlib import Path

import pytest

from app.budget import BUDGET_EXHAUSTED, SEGMENT_DEADLINE, LlmBudget
from app.limits import (
    LLM_MIN_BUDGET_S,
    LLM_RESERVE_S,
    MAX_REPLANS,
    PLANNER_DEADLINE_S,
    RUN_TIMEOUT_S,
    WORKER_DEADLINE_S,
)
from app.workers.planner import LlmPlanner
from tests.fake_llm import FakePlannerModel, plan
from tests.harness import Harness

# Anchored to this file (tests/ -> agent-service/ -> repo root), never the working directory.
BACKEND = Path(__file__).resolve().parents[2] / "backend" / "CampusSpace.Api"
WATCHDOG_MARGIN_S = 60  # .NET's watchdog must fire well after the agent service's own timeout


class Clock:
    def __init__(self) -> None:
        self.now = 1000.0

    def __call__(self) -> float:
        return self.now


def test_no_segment_deadline_means_the_step_deadline() -> None:
    assert LlmBudget(None).allow(45.0) == 45.0


def test_the_step_gets_its_own_deadline_while_the_segment_has_room() -> None:
    clock = Clock()
    budget = LlmBudget(clock.now + RUN_TIMEOUT_S, clock)

    assert budget.allow(PLANNER_DEADLINE_S) == PLANNER_DEADLINE_S


def test_the_step_is_cut_to_the_segment_minus_the_reserve() -> None:
    clock = Clock()
    budget = LlmBudget(clock.now + 80, clock)

    assert budget.allow(PLANNER_DEADLINE_S) == 80 - LLM_RESERVE_S


def test_below_the_minimum_the_llm_is_skipped() -> None:
    clock = Clock()
    budget = LlmBudget(clock.now + LLM_RESERVE_S + LLM_MIN_BUDGET_S - 0.1, clock)

    assert budget.allow(WORKER_DEADLINE_S) is None


def test_a_short_step_deadline_is_not_the_minimum() -> None:
    # The minimum applies to the time left in the segment, not to a (test-sized) step deadline.
    clock = Clock()
    assert LlmBudget(clock.now + RUN_TIMEOUT_S, clock).allow(0.2) == 0.2


def test_from_config_reads_the_runner_deadline() -> None:
    clock = Clock()
    config = {"configurable": {"thread_id": "t", SEGMENT_DEADLINE: clock.now + 100}}

    assert LlmBudget.from_config(config, clock).deadline == clock.now + 100
    assert LlmBudget.from_config({"configurable": {}}, clock).deadline is None


def simulate(llm_steps_per_plan: list[float]) -> tuple[float, list[float | None]]:
    """1 plan + MAX_REPLANS re-plans, every LLM call hanging for as long as it is allowed.
    Returns the segment time when the last LLM wait ends and every step's allowance."""
    clock = Clock()
    budget = LlmBudget(clock.now + RUN_TIMEOUT_S, clock)
    started = clock.now
    allowed: list[float | None] = []
    for _ in range(1 + MAX_REPLANS):
        for own in llm_steps_per_plan:
            wait = budget.allow(own)
            allowed.append(wait)
            clock.now += wait or 0.0  # hangs until its deadline; a skipped step uses the stub
    return clock.now - started, allowed


def test_worst_case_planner_and_venue_all_hanging_fit_the_run_timeout() -> None:
    elapsed, allowed = simulate([PLANNER_DEADLINE_S, WORKER_DEADLINE_S])

    # 60 (plan) + 45 (venue) + 45 (re-plan 1, cut) = 150; everything after is skipped.
    assert allowed == [60.0, 45.0, 45.0, None, None, None]
    assert elapsed == RUN_TIMEOUT_S - LLM_RESERVE_S
    assert elapsed + LLM_RESERVE_S <= RUN_TIMEOUT_S


def test_worst_case_holds_with_every_worker_on_an_llm() -> None:
    # Task 4.4 adds a third LLM worker; the bound does not depend on how many there are.
    elapsed, _ = simulate([PLANNER_DEADLINE_S] + [WORKER_DEADLINE_S] * 3)

    assert elapsed <= RUN_TIMEOUT_S - LLM_RESERVE_S


def test_worst_case_with_the_planner_venue_and_equipment_llms_all_hanging() -> None:
    elapsed, allowed = simulate([PLANNER_DEADLINE_S, WORKER_DEADLINE_S, WORKER_DEADLINE_S])

    # 60 (plan) + 45 (venue) + 45 (equipment) = 150 = RUN_TIMEOUT_S - reserve; every LLM step of
    # the two re-plans is skipped (stubs), so the run still ends inside RUN_TIMEOUT_S.
    assert allowed == [60.0, 45.0, 45.0, None, None, None, None, None, None]
    assert elapsed == RUN_TIMEOUT_S - LLM_RESERVE_S


def dotnet_run_timeout_minutes() -> list[int]:
    """Every RunTimeoutMinutes .NET could use: the options default and any appsettings value."""
    options = (BACKEND / "Options" / "AgentServiceOptions.cs").read_text()
    found = [int(m) for m in re.findall(r"RunTimeoutMinutes \{ get; set; \} = (\d+);", options)]
    for settings in BACKEND.glob("appsettings*.json"):
        section = json.loads(settings.read_text()).get("AgentService") or {}
        if "RunTimeoutMinutes" in section:
            found.append(int(section["RunTimeoutMinutes"]))
    return found


def test_the_dotnet_watchdog_stays_a_minute_above_the_run_timeout() -> None:
    minutes = dotnet_run_timeout_minutes()

    assert minutes, "AgentServiceOptions.RunTimeoutMinutes default not found"
    for value in minutes:
        assert value * 60 >= RUN_TIMEOUT_S + WATCHDOG_MARGIN_S


def test_an_exhausted_budget_skips_the_planner_llm(tmp_path: Path) -> None:
    model = FakePlannerModel(plan(["prefer the New Building"]))
    # 35 s segment: 35 - 30 reserve = 5 s < the 10 s minimum.
    h = Harness(tmp_path / "t.sqlite", run_timeout_s=35,
                planner=LlmPlanner(lambda: model, "gemini-3.5-flash"))  # fmt: skip
    try:
        view = h.view(h.start())
    finally:
        h.close()

    out = next(s for s in view.steps if s["agent_name"] == "supervisor")["output"]
    assert model.calls == []
    assert out["planner"] == "fallback" and out["fallback_reason"] == BUDGET_EXHAUSTED
    assert out["attempts"] == 0 and out["usage"] is None
    assert view.status == "awaiting_approval"


@pytest.mark.parametrize("left", [LLM_RESERVE_S + LLM_MIN_BUDGET_S, RUN_TIMEOUT_S])
def test_the_minimum_itself_is_allowed(left: float) -> None:
    clock = Clock()
    assert LlmBudget(clock.now + left, clock).allow(WORKER_DEADLINE_S) is not None
