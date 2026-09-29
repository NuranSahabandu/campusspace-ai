"""Optional: ONE real Gemini planner call. Skipped unless both are in the environment:

    RUN_LIVE_LLM=1 uv run --env-file ../.env pytest -m live_llm -s

(--env-file loads the key without putting it on the command line). The request context comes from
the fake .NET API (request 42), so no API is needed. Never run in CI.
"""

import os
import time

import pytest
from pydantic import SecretStr

from app.config import DEFAULT_PLANNER_MODEL
from app.llm import build_chat_model
from app.schemas import Plan
from app.tools import ToolClient, build_tools
from app.workers.planner import LlmPlanner, PlannerInput
from app.workers.supervisor import enforce_plan_rules, load_context
from tests.conftest import make_settings
from tests.fake_api import FakeCampusApi
from tests.keys import TEST_TOOLS_KEY

# Read at import, before the autouse clean_env fixture removes it from the environment.
_KEY = os.environ.get("GOOGLE_API_KEY", "")
_ENABLED = os.environ.get("RUN_LIVE_LLM") == "1"
_MODEL = os.environ.get("PLANNER_MODEL") or DEFAULT_PLANNER_MODEL

pytestmark = [
    pytest.mark.live_llm,
    pytest.mark.skipif(
        not (_KEY and _ENABLED), reason="set GOOGLE_API_KEY and RUN_LIVE_LLM=1 to call Gemini"
    ),
]


def test_one_real_planner_call_returns_a_valid_plan(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = make_settings(
        monkeypatch, AGENT_LLM_AGENTS="supervisor", GOOGLE_API_KEY=_KEY, PLANNER_MODEL=_MODEL
    )
    api = FakeCampusApi()
    api.requests[42]["notes"] = "Prefer a room near the main building, and a cheaper one please."
    client = ToolClient("http://api.test", SecretStr(TEST_TOOLS_KEY), transport=api.transport())
    try:
        ctx = load_context(build_tools(client), 42)
    finally:
        client.close()
    planner = LlmPlanner(lambda: build_chat_model("planner", settings), settings.planner_model)

    started = time.perf_counter()
    outcome = planner.plan(PlannerInput(request=ctx["request"], catalogs=ctx["catalogs"]))
    latency = time.perf_counter() - started
    plan, corrections = enforce_plan_rules(outcome.plan, ctx["request"], ctx["catalogs"])

    print(
        f"\nmodel={outcome.model} mode={outcome.mode} attempts={outcome.attempts} "
        f"latency={latency:.2f}s usage={outcome.usage}\n"
        f"soft_preferences={plan.soft_preferences}\ncorrections={corrections}\n"
        f"steps={[s.agent for s in plan.steps]}"
    )
    assert outcome.mode == "llm", outcome.fallback_reason
    Plan.model_validate(outcome.plan.model_dump())
    assert outcome.usage and outcome.usage["total_tokens"] > 0
    assert [s.agent for s in plan.steps] == [
        "venue_matching",
        "equipment_allocation",
        "policy_cost",
    ]
