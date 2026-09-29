import json
import platform

import pytest
from fastapi.testclient import TestClient

from app import __version__
from app.main import create_app
from tests.conftest import TEST_SERVICE_KEY, TEST_TOOLS_KEY, make_settings


def test_health_returns_200_without_key(client: TestClient) -> None:
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "service": "agent-service",
        "version": __version__,
        "python": platform.python_version(),
        "agents": {
            "supervisor": "stub",
            "venue_matching": "stub",
            "equipment_allocation": "stub",
            "policy_cost": "stub",
        },
        "models": {"planner": "gemini-2.5-flash", "worker": "gemini-2.5-flash-lite"},
        "checkpointer": "sqlite",
        "checkpointer_ok": True,
        "google_api_key_configured": False,
    }


def test_health_never_returns_secret_values(monkeypatch: pytest.MonkeyPatch) -> None:
    google_key = "fake-google-key-value-123"
    settings = make_settings(monkeypatch, GOOGLE_API_KEY=google_key)

    with TestClient(create_app(settings)) as c:
        response = c.get("/health")

    assert response.status_code == 200
    assert response.json()["google_api_key_configured"] is True
    for secret in (TEST_SERVICE_KEY, google_key, TEST_TOOLS_KEY):
        assert secret not in response.text


def test_startup_creates_the_checkpoint_file(monkeypatch: pytest.MonkeyPatch, tmp_path) -> None:
    path = tmp_path / "nested" / "cp.sqlite"
    settings = make_settings(monkeypatch, AGENT_CHECKPOINT_PATH=str(path))

    with TestClient(create_app(settings)) as c:
        assert c.get("/health").json()["checkpointer_ok"] is True
    assert path.exists()


def test_health_shows_the_llm_supervisor_without_building_a_model(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    settings = make_settings(
        monkeypatch, AGENT_LLM_AGENTS="supervisor", GOOGLE_API_KEY="fake-google-key-value-123"
    )

    def no_model(*_: object) -> None:
        raise AssertionError("health and startup must not build a model")

    monkeypatch.setattr("app.main.build_chat_model", no_model)
    with TestClient(create_app(settings)) as c:
        body = c.get("/health").json()

    assert body["agents"] == {
        "supervisor": "llm",
        "venue_matching": "stub",
        "equipment_allocation": "stub",
        "policy_cost": "stub",
    }
    assert body["models"] == {"planner": "gemini-2.5-flash", "worker": "gemini-2.5-flash-lite"}
    assert "fake-google-key-value-123" not in json.dumps(body)
