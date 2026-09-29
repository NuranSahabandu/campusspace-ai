"""Optional: real Gemini calls (planner, venue worker, thinking comparison). Skipped unless
both are in the environment:

    RUN_LIVE_LLM=1 uv run --env-file ../.env pytest -m live_llm -s

(--env-file loads the key without putting it on the command line). The request context comes from
the fake .NET API (request 42), so no API is needed. Never run in CI.
"""

import os
import time

import pytest
from pydantic import SecretStr

from app.budget import UNLIMITED
from app.config import DEFAULT_PLANNER_MODEL, DEFAULT_WORKER_MODEL
from app.llm import build_chat_model
from app.schemas import Plan, VenueResult
from app.tools import ToolClient, build_tools, recording
from app.workers.planner import LlmPlanner, PlannerInput
from app.workers.supervisor import INSTRUCTIONS, build_brief, enforce_plan_rules, load_context
from app.workers.venue_llm import LlmVenueWorker
from tests.conftest import make_settings
from tests.fake_api import FakeCampusApi
from tests.keys import TEST_TOOLS_KEY

# Read at import, before the autouse clean_env fixture removes it from the environment.
_KEY = os.environ.get("GOOGLE_API_KEY", "")
_ENABLED = os.environ.get("RUN_LIVE_LLM") == "1"
_MODEL = os.environ.get("PLANNER_MODEL") or DEFAULT_PLANNER_MODEL
_WORKER = os.environ.get("WORKER_MODEL") or DEFAULT_WORKER_MODEL

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


def venue_task(api: FakeCampusApi, soft_preferences: list[str]) -> tuple[str, dict]:
    """The venue brief for request 42 exactly as the graph builds it."""
    client = ToolClient("http://api.test", SecretStr(TEST_TOOLS_KEY), transport=api.transport())
    try:
        ctx = load_context(build_tools(client), 42)
    finally:
        client.close()
    state = ctx | {"requirements": {"soft_preferences": soft_preferences}, "excluded_room_ids": []}
    step = {"agent": "venue_matching", "task": INSTRUCTIONS["venue_matching"]}
    return build_brief(step, state), ctx


@pytest.mark.parametrize(
    ("preferences", "expected"),
    [([], {"A301", "N201"}), (["prefer the New Building"], {"N201"})],
)
def test_real_venue_worker_on_the_demo_request(
    monkeypatch: pytest.MonkeyPatch, preferences: list[str], expected: set[str]
) -> None:
    settings = make_settings(
        monkeypatch, AGENT_LLM_AGENTS="venue_matching", GOOGLE_API_KEY=_KEY, WORKER_MODEL=_WORKER
    )
    api = FakeCampusApi()
    task, _ = venue_task(api, preferences)
    client = ToolClient("http://api.test", SecretStr(TEST_TOOLS_KEY), transport=api.transport())
    worker = LlmVenueWorker(lambda: build_chat_model("worker", settings), settings.worker_model)
    try:
        started = time.perf_counter()
        with recording() as rec:
            attempt = worker.run(task, build_tools(client), UNLIMITED)
        latency = time.perf_counter() - started
    finally:
        client.close()

    meta, result = attempt.meta, attempt.result
    print(
        f"\npreferences={preferences} model={meta['model']} thinking={settings.worker_thinking} "
        f"mode={meta['mode']} attempts={meta['attempts']} latency={latency:.2f}s\n"
        f"usage={meta['usage']}\ncorrections={meta['corrections']}\n"
        f"tools={[c.tool_name for c in rec.calls]}\n"
        f"options={[(o['code'], o['reason']) for o in (result or {}).get('options', [])]}"
    )
    assert meta["mode"] == "llm", meta.get("fallback_reason")
    VenueResult.model_validate(result)
    assert result["options"][0]["code"] in expected
    assert rec.calls and rec.calls[0].tool_name == "search_available_rooms"


def test_planner_thinking_low_uses_fewer_output_tokens_than_medium(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The thinking cap: the same planner call at medium (gemini-3.5-flash's default) and low."""
    api = FakeCampusApi()
    _, ctx = venue_task(api, [])
    results = {}
    for level in ("medium", "low"):
        settings = make_settings(
            monkeypatch,
            AGENT_LLM_AGENTS="supervisor",
            GOOGLE_API_KEY=_KEY,
            PLANNER_MODEL=_MODEL,
            PLANNER_THINKING=level,
        )
        planner = LlmPlanner(lambda s=settings: build_chat_model("planner", s), _MODEL)
        started = time.perf_counter()
        outcome = planner.plan(PlannerInput(request=ctx["request"], catalogs=ctx["catalogs"]))
        results[level] = (outcome, time.perf_counter() - started)
        print(f"\nthinking={level} mode={outcome.mode} latency={results[level][1]:.2f}s "
              f"usage={outcome.usage}")  # fmt: skip
        assert outcome.mode == "llm", outcome.fallback_reason

    medium, low = results["medium"][0].usage, results["low"][0].usage
    assert low["output_tokens"] <= medium["output_tokens"]
