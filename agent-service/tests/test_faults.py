"""Development-only fault injection (app/faults.py, Task 5.5) through the REAL Gemini client stack:
ChatGoogleGenerativeAI with a fake key, google-genai's retry, our retry, deadlines and fallbacks.
Nothing here reaches the network: the faults that forward use a MockTransport upstream."""

import json
import logging
import re
from pathlib import Path

import httpx
import pytest
from pydantic import ValidationError

from app import faults
from app.config import Settings
from app.faults import INVALID_KEY, MISSING_MODEL, FaultTransport
from app.llm import build_chat_model
from app.workers.planner import LlmPlanner
from app.workers.venue_llm import LlmVenueWorker
from tests.conftest import make_settings
from tests.harness import Harness
from tests.test_planner import steps_of

FAKE_GOOGLE_KEY = "fake-google-key-" + "g" * 24
ALL = "supervisor,venue_matching,equipment_allocation,policy_cost"
CI = Path(__file__).resolve().parents[2] / ".github" / "workflows" / "ci.yml"
GEMINI = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent"


def fault_settings(
    monkeypatch: pytest.MonkeyPatch, fault: str, agents: str, **env: str
) -> Settings:
    base = {"AGENT_LLM_AGENTS": ALL, "GOOGLE_API_KEY": FAKE_GOOGLE_KEY, "AGENT_ENV": "development",
            "AGENT_FAULT": fault, "AGENT_FAULT_AGENTS": agents}  # fmt: skip
    return make_settings(monkeypatch, **(base | env))


@pytest.fixture
def no_backoff(monkeypatch: pytest.MonkeyPatch) -> None:
    """google-genai's tenacity retry sleeps through tenacity.nap -> time.sleep."""
    monkeypatch.setattr("tenacity.nap.time.sleep", lambda _s: None)


def ok_response(request: httpx.Request) -> httpx.Response:
    part = {"text": "hello"}
    return httpx.Response(200, request=request, json={
        "candidates": [{"content": {"role": "model", "parts": [part]}, "finishReason": "STOP"}],
    })  # fmt: skip


# ---------- settings ----------


def test_fault_injection_is_off_by_default(monkeypatch: pytest.MonkeyPatch) -> None:
    s = make_settings(monkeypatch, AGENT_LLM_AGENTS=ALL, GOOGLE_API_KEY=FAKE_GOOGLE_KEY)

    assert s.fault_summary() is None
    assert s.fault_for("supervisor") is None


@pytest.mark.parametrize("env", ["", "production", "Staging"])
def test_a_fault_outside_development_refuses_to_start(monkeypatch, env: str) -> None:
    with pytest.raises(ValidationError, match="AGENT_FAULT is for development only"):
        fault_settings(monkeypatch, "rate_limit", "supervisor", AGENT_ENV=env)


def test_an_unknown_fault_refuses_to_start(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError, match="AGENT_FAULT must be one of"):
        fault_settings(monkeypatch, "meteor", "supervisor")


def test_a_fault_needs_target_agents(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError, match="AGENT_FAULT_AGENTS must name"):
        fault_settings(monkeypatch, "slow", "")


def test_a_fault_on_a_stub_agent_refuses_to_start(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError, match=r"not LLM agents \(policy_cost\)"):
        fault_settings(monkeypatch, "slow", "policy_cost", AGENT_LLM_AGENTS="supervisor")


def test_the_fault_applies_to_its_agents_only(monkeypatch: pytest.MonkeyPatch) -> None:
    s = fault_settings(monkeypatch, "Rate_Limit", " equipment_allocation ", AGENT_FAULT_TIMES="2")

    assert s.fault_for("equipment_allocation") == "rate_limit"
    assert s.fault_for("supervisor") is None
    assert s.fault_summary() == {"fault": "rate_limit", "agents": ["equipment_allocation"],
                                 "times": 2}  # fmt: skip


def test_ci_never_turns_on_faults_or_llm_agents() -> None:
    text = CI.read_text()
    assert "AGENT_FAULT" not in text and "AGENT_ENV" not in text
    # An env entry with any value (YAML "AGENT_LLM_AGENTS: x"); empty or absent keeps all stubs.
    assert not re.search(r"AGENT_LLM_AGENTS\s*[:=]\s*['\"]?[a-z]", text)


# ---------- the transport ----------


def test_rate_limit_is_a_gemini_429_with_or_without_retry_after() -> None:
    request = httpx.Request("POST", GEMINI, content=b"{}")
    plain = FaultTransport("rate_limit", "x").handle_request(request)
    later = FaultTransport("rate_limit_retry_after", "x").handle_request(request)

    assert plain.status_code == later.status_code == 429
    assert "retry-after" not in plain.headers
    assert later.headers["retry-after"] == faults.RETRY_AFTER_S
    assert plain.json()["error"]["status"] == "RESOURCE_EXHAUSTED"


def test_slow_sleeps_past_every_deadline_then_times_out() -> None:
    slept: list[float] = []
    transport = FaultTransport("slow", "x", sleep=slept.append)

    with pytest.raises(httpx.ReadTimeout):
        transport.handle_request(httpx.Request("POST", GEMINI, content=b"{}"))
    assert slept == [faults.SLOW_S] and faults.SLOW_S > 60


def test_bad_key_and_model_not_found_forward_a_broken_request() -> None:
    seen: list[httpx.Request] = []
    upstream = httpx.MockTransport(lambda r: seen.append(r) or httpx.Response(400, request=r))
    request = httpx.Request("POST", GEMINI, headers={"x-goog-api-key": FAKE_GOOGLE_KEY},
                            content=b"{}")  # fmt: skip

    FaultTransport("bad_key", "x", inner=upstream).handle_request(request)
    FaultTransport("model_not_found", "x", inner=upstream).handle_request(
        httpx.Request("POST", GEMINI, headers={"x-goog-api-key": FAKE_GOOGLE_KEY}, content=b"{}")
    )

    assert seen[0].headers["x-goog-api-key"] == INVALID_KEY
    assert seen[1].url.path == f"/v1beta/models/{MISSING_MODEL}:generateContent"
    assert seen[1].headers["x-goog-api-key"] == FAKE_GOOGLE_KEY


def test_times_faults_only_the_first_calls_then_passes_through() -> None:
    upstream = httpx.MockTransport(ok_response)
    transport = FaultTransport("rate_limit", "x", times=2, inner=upstream)

    codes = [transport.handle_request(httpx.Request("POST", GEMINI, content=b"{}")).status_code
             for _ in range(4)]  # fmt: skip

    assert codes == [429, 429, 200, 200]
    assert (transport.calls, transport.injected) == (4, 2)


def test_malformed_answers_the_structured_tool_with_bad_args() -> None:
    body = {"tools": [{"functionDeclarations": [{"name": "search_available_rooms"},
                                                {"name": "VenueResult"}]}]}  # fmt: skip
    worker = FaultTransport("malformed", "x").handle_request(
        httpx.Request("POST", GEMINI, content=json.dumps(body).encode())
    )
    planner = FaultTransport("malformed", "x").handle_request(
        httpx.Request("POST", GEMINI, content=b'{"contents": []}')
    )

    part = worker.json()["candidates"][0]["content"]["parts"][0]
    assert part["functionCall"] == {"name": "VenueResult", "args": {"bogus": 1}}
    assert "not-a-list" in planner.json()["candidates"][0]["content"]["parts"][0]["text"]


# ---------- through the real client ----------


def test_only_the_target_agents_client_gets_the_fault(monkeypatch: pytest.MonkeyPatch) -> None:
    s = fault_settings(monkeypatch, "connection", "venue_matching")

    faulty = build_chat_model("worker", s, "venue_matching")
    clean = build_chat_model("worker", s, "equipment_allocation")

    assert isinstance(faulty.client_args["transport"], FaultTransport)
    assert not (clean.client_args or {}).get("transport")


def test_a_429_storm_is_retried_by_the_client_then_the_planner_falls_back(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path, no_backoff, caplog
) -> None:
    s = fault_settings(monkeypatch, "rate_limit", "supervisor")
    planner = LlmPlanner(lambda: build_chat_model("planner", s, "supervisor"), s.planner_model)
    h = Harness(tmp_path / "cp.sqlite", planner=planner)
    caplog.set_level(logging.WARNING, logger="agent_service.faults")
    try:
        view = h.view(h.start())
    finally:
        h.close()

    out = steps_of(view, "supervisor")[0]["output"]
    assert view.status == "awaiting_approval"
    assert out["planner"] == "fallback" and out["planner_fallback"] is True
    assert out["fallback_reason"] == "Planner LLM error: rate limited (HTTP 429)"
    # max_retries=3 is google-genai HttpRetryOptions(attempts=3): three calls in all.
    injected = [r for r in caplog.records if "rate_limit on supervisor" in r.getMessage()]
    assert len(injected) == 3


def test_malformed_output_is_retried_once_then_the_venue_worker_falls_back(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    s = fault_settings(monkeypatch, "malformed", "venue_matching")
    worker = LlmVenueWorker(lambda: build_chat_model("worker", s, "venue_matching"),
                            s.worker_model)  # fmt: skip
    h = Harness(tmp_path / "cp.sqlite", workers={"venue_matching": worker})
    try:
        view = h.view(h.start())
    finally:
        h.close()

    out = steps_of(view, "venue_matching")[0]["output"]
    assert view.status == "awaiting_approval"
    assert out["mode"] == "fallback" and out["attempts"] == 2
    assert out["fallback_reason"].startswith("Venue output invalid twice: VenueResult")


def test_a_slow_call_hits_the_planner_deadline(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    monkeypatch.setattr(faults, "SLOW_S", 1.0)
    s = fault_settings(monkeypatch, "slow", "supervisor")
    planner = LlmPlanner(lambda: build_chat_model("planner", s, "supervisor"), s.planner_model,
                         deadline_s=0.2)  # fmt: skip
    h = Harness(tmp_path / "cp.sqlite", planner=planner)
    try:
        view = h.view(h.start())
    finally:
        h.close()

    out = steps_of(view, "supervisor")[0]["output"]
    assert out["fallback_reason"] == "Planner LLM timed out after 0.2 s"
    assert view.status == "awaiting_approval"


def test_the_fault_is_logged_at_startup_and_shown_on_health_by_name_only(
    monkeypatch: pytest.MonkeyPatch, caplog
) -> None:
    from fastapi.testclient import TestClient

    from app.main import create_app

    s = fault_settings(monkeypatch, "slow", "supervisor,policy_cost")
    caplog.set_level(logging.WARNING, logger="agent_service.main")
    with TestClient(create_app(s)) as c:
        health = c.get("/health").json()

    assert health["fault_injection"] == {"fault": "slow", "agents": ["supervisor", "policy_cost"],
                                         "times": 0}  # fmt: skip
    assert "FAULT INJECTION ACTIVE: fault=slow agents=supervisor,policy_cost" in caplog.text
    assert FAKE_GOOGLE_KEY not in caplog.text


def test_malformed_planner_output_reason_never_quotes_the_completion(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    s = fault_settings(monkeypatch, "malformed", "supervisor")
    planner = LlmPlanner(lambda: build_chat_model("planner", s, "supervisor"), s.planner_model)
    h = Harness(tmp_path / "cp.sqlite", planner=planner)
    try:
        view = h.view(h.start())
    finally:
        h.close()

    reason = steps_of(view, "supervisor")[0]["output"]["fallback_reason"]
    assert reason.startswith("Planner output invalid twice: ")
    assert "not-a-list" not in reason and "bogus" not in reason
    assert reason.endswith("Field required")


def test_provider_error_text_never_reaches_the_trace(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path, no_backoff
) -> None:
    s = fault_settings(monkeypatch, "rate_limit", "venue_matching")
    worker = LlmVenueWorker(lambda: build_chat_model("worker", s, "venue_matching"),
                            s.worker_model)  # fmt: skip
    h = Harness(tmp_path / "cp.sqlite", workers={"venue_matching": worker})
    try:
        tid = h.start()
        view, state = h.view(tid), h.state_json(tid)
    finally:
        h.close()

    out = steps_of(view, "venue_matching")[0]["output"]
    assert out["fallback_reason"] == "Venue LLM error: rate limited (HTTP 429)"
    assert "RESOURCE_EXHAUSTED" not in state and "{'error'" not in state


def test_a_timeout_reason_is_rounded(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    monkeypatch.setattr(faults, "SLOW_S", 1.0)
    s = fault_settings(monkeypatch, "slow", "supervisor")
    planner = LlmPlanner(lambda: build_chat_model("planner", s, "supervisor"), s.planner_model,
                         deadline_s=0.2123456)  # fmt: skip
    h = Harness(tmp_path / "cp.sqlite", planner=planner)
    try:
        view = h.view(h.start())
    finally:
        h.close()

    reason = steps_of(view, "supervisor")[0]["output"]["fallback_reason"]
    assert reason == "Planner LLM timed out after 0.212 s"
