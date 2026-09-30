"""Optional: real Gemini calls (planner, venue, equipment and policy workers, thinking comparison,
and one full four-agent run).
Skipped unless both are in the environment:

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
from app.schemas import EquipmentResult, Plan, VenueResult
from app.tools import ToolClient, build_tools, recording
from app.workers.equipment_llm import LlmEquipmentWorker
from app.workers.planner import LlmPlanner, PlannerInput
from app.workers.policy_llm import LlmPolicyWorker
from app.workers.supervisor import INSTRUCTIONS, build_brief, enforce_plan_rules, load_context
from app.workers.venue_llm import LlmVenueWorker
from tests.conftest import make_settings
from tests.fake_api import FakeCampusApi
from tests.harness import Harness, latest
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


def equipment_task(api: FakeCampusApi) -> str:
    """The equipment brief for request 42 in A301 (computers, projector), as the graph builds it."""
    _, ctx = venue_task(api, [])
    a301 = api.room("A301")
    chosen = {
        "room_id": a301["id"],
        "code": "A301",
        "features": [f["code"] for f in a301["features"]],
    }
    step = {"agent": "equipment_allocation", "task": INSTRUCTIONS["equipment_allocation"]}
    return build_brief(step, ctx | {"venue": {"options": [chosen], "unmet": None}})


@pytest.mark.parametrize("short", [False, True], ids=["demo", "mics-short"])
def test_real_equipment_worker_on_the_demo_lines(
    monkeypatch: pytest.MonkeyPatch, short: bool
) -> None:
    settings = make_settings(
        monkeypatch,
        AGENT_LLM_AGENTS="equipment_allocation",
        GOOGLE_API_KEY=_KEY,
        WORKER_MODEL=_WORKER,
    )
    api = FakeCampusApi()
    if short:
        api.reserved = {"MIC-WIRELESS": 6}  # 1 of 7 left; MIC-WIRED has 8
    task = equipment_task(api)
    client = ToolClient("http://api.test", SecretStr(TEST_TOOLS_KEY), transport=api.transport())
    worker = LlmEquipmentWorker(lambda: build_chat_model("worker", settings), settings.worker_model)
    try:
        started = time.perf_counter()
        with recording() as rec:
            attempt = worker.run(task, build_tools(client), UNLIMITED)
        latency = time.perf_counter() - started
    finally:
        client.close()

    meta, result = attempt.meta, attempt.result
    print(
        f"\nshort={short} model={meta['model']} thinking={settings.worker_thinking} "
        f"mode={meta['mode']} attempts={meta['attempts']} latency={latency:.2f}s\n"
        f"usage={meta['usage']}\ncorrections={meta['corrections']}\n"
        f"tools={[c.tool_name for c in rec.calls]}\nresult={result}"
    )
    assert meta["mode"] == "llm", meta.get("fallback_reason")
    EquipmentResult.model_validate(result)
    assert {"type_code": "PROJ-PORTABLE", "qty": 0, "source": "room_builtin"} in result["lines"]
    if not short:
        assert {"type_code": "MIC-WIRELESS", "qty": 2, "source": "portable"} in result["lines"]
    else:
        wired = {"type_code": "MIC-WIRED", "qty": 2, "source": "substitute"}
        assert wired in result["lines"] or any(
            u.startswith("MIC-WIRELESS") for u in result["unmet"]
        )


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


# ---------- Task 4.4: policy worker and the full four-agent run ----------

# USD per 1M tokens, paid tier, standard; output includes thinking tokens. Source:
# https://ai.google.dev/gemini-api/docs/pricing (checked 2026-09-30). An ESTIMATE for the report.
PRICES = {"gemini-3.5-flash": (1.50, 9.00), "gemini-3.5-flash-lite": (0.30, 2.50)}


def cost_usd(model: str, usage: dict | None) -> float:
    if not usage or model not in PRICES:
        return 0.0
    price_in, price_out = PRICES[model]
    return (usage["input_tokens"] * price_in + usage["output_tokens"] * price_out) / 1_000_000


def report(view) -> float:
    """Per-step latency, tokens, mode and corrections; returns the run's estimated cost."""
    total = 0.0
    for step in view.steps:
        out = step["output"] or {}
        usage = out.get("usage")
        model = out.get("model", "")
        cost = cost_usd(model, usage)
        total += cost
        print(f"  #{step['sequence']:>2} {step['agent_name']:<21} {step['duration_ms']:>6} ms "
              f"mode={out.get('mode') or out.get('planner') or '-':<8} "
              f"tokens={usage and (usage['input_tokens'], usage['output_tokens'])} "
              f"~${cost:.5f} tools={[c['tool_name'] for c in step['tool_calls']]}")  # fmt: skip
        for c in out.get("corrections") or []:
            print(f"       correction: {c}")
        if out.get("fallback_reason"):
            print(f"       fallback: {out['fallback_reason']}")
    return total


def summary_replacements(view) -> list[str]:
    """User fix 2: every summary/flag the checks replaced or dropped, with why."""
    [step] = [s for s in view.steps if s["agent_name"] == "policy_cost"]
    return [c for c in (step["output"] or {}).get("corrections", [])
            if c.startswith(("officer_summary", "flag", "policy_flags"))]  # fmt: skip


@pytest.mark.parametrize("request_id", [42, 43], ids=["student", "lecturer"])
def test_real_policy_worker_on_the_demo(
    monkeypatch: pytest.MonkeyPatch, tmp_path, request_id: int
) -> None:
    settings = make_settings(
        monkeypatch, AGENT_LLM_AGENTS="policy_cost", GOOGLE_API_KEY=_KEY, WORKER_MODEL=_WORKER
    )
    worker = LlmPolicyWorker(lambda: build_chat_model("worker", settings), settings.worker_model)
    h = Harness(tmp_path / "p.sqlite", FakeCampusApi(), workers={"policy_cost": worker})
    try:
        view = h.view(h.start(request_id))
    finally:
        h.close()

    [step] = [s for s in view.steps if s["agent_name"] == "policy_cost"]
    out = step["output"]
    print(f"\nrequest={request_id} status={view.status}")
    report(view)
    print(f"summary={view.proposal and view.proposal['officer_summary']!r}\n"
          f"flags={view.proposal and view.proposal['policy_flags']}\n"
          f"replaced/dropped={summary_replacements(view)}")  # fmt: skip
    assert out["mode"] == "llm", out.get("fallback_reason")
    assert view.status == "awaiting_approval"
    assert all(v["passed"] for v in latest(view))
    total = view.proposal["quote"]["total"]
    assert total == ("5500.00" if request_id == 42 else "0.00")
    assert [c["tool_name"] for c in step["tool_calls"]].count("calculate_quote") >= 1


def test_full_graph_with_all_four_llm_agents(monkeypatch: pytest.MonkeyPatch, tmp_path) -> None:
    """The Phase 4 exit check: every agent on Gemini, over the fake API, request 42."""
    settings = make_settings(
        monkeypatch,
        AGENT_LLM_AGENTS="supervisor,venue_matching,equipment_allocation,policy_cost",
        GOOGLE_API_KEY=_KEY,
        PLANNER_MODEL=_MODEL,
        WORKER_MODEL=_WORKER,
    )

    def worker_model():
        return build_chat_model("worker", settings)

    planner = LlmPlanner(lambda: build_chat_model("planner", settings), settings.planner_model)
    workers = {
        "venue_matching": LlmVenueWorker(worker_model, settings.worker_model),
        "equipment_allocation": LlmEquipmentWorker(worker_model, settings.worker_model),
        "policy_cost": LlmPolicyWorker(worker_model, settings.worker_model),
    }
    h = Harness(tmp_path / "full.sqlite", FakeCampusApi(), planner=planner, workers=workers,
                model_label=settings.model_label())  # fmt: skip
    try:
        started = time.perf_counter()
        view = h.view(h.start())
        wall = time.perf_counter() - started
    finally:
        h.close()

    print(f"\nstatus={view.status} model={view.model} wall={wall:.1f}s "
          f"duration_ms={view.duration_ms} usage={view.usage}")  # fmt: skip
    cost = report(view)
    passed = sum(v["passed"] for v in latest(view))
    print(f"rules passed={passed}/12 total={view.proposal and view.proposal['quote']['total']} "
          f"estimated cost=${cost:.5f}\nsummary={view.officer_summary!r}\n"
          f"flags={view.proposal and view.proposal['policy_flags']}\n"
          f"replaced/dropped={summary_replacements(view)}")  # fmt: skip
    assert view.status == "awaiting_approval", view.error
    assert passed == 12
    assert view.proposal["quote"]["total"] == "5500.00"
    modes = {s["agent_name"]: (s["output"] or {}).get("mode") for s in view.steps}
    assert modes["venue_matching"] == modes["equipment_allocation"] == "llm"
    assert modes["policy_cost"] == "llm"
