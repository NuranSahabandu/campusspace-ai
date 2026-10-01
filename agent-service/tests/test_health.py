import json
import logging
import platform

import pytest
from fastapi.testclient import TestClient
from langgraph.checkpoint.memory import InMemorySaver

from app import __version__
from app.checkpoint import CheckpointUnavailable
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
        "models": {"planner": "gemini-3.5-flash", "worker": "gemini-3.5-flash-lite"},
        "thinking": {"planner": "low", "worker": "minimal"},
        "checkpointer": "sqlite",
        "checkpointer_ok": True,
        "google_api_key_configured": False,
        "fault_injection": None,
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
        monkeypatch,
        AGENT_LLM_AGENTS="supervisor,venue_matching,equipment_allocation",
        GOOGLE_API_KEY="fake-google-key-value-123",
    )

    def no_model(*_: object) -> None:
        raise AssertionError("health and startup must not build a model")

    monkeypatch.setattr("app.main.build_chat_model", no_model)
    with TestClient(create_app(settings)) as c:
        body = c.get("/health").json()

    assert body["agents"] == {
        "supervisor": "llm",
        "venue_matching": "llm",
        "equipment_allocation": "llm",
        "policy_cost": "stub",
    }
    assert body["models"] == {"planner": "gemini-3.5-flash", "worker": "gemini-3.5-flash-lite"}
    assert "fake-google-key-value-123" not in json.dumps(body)


def test_health_names_the_postgres_checkpointer_without_its_url(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    password = "sentinel-db-password-" + "p" * 16
    url = f"postgresql://campusspace_agent:{password}@db.test:5432/agent"
    settings = make_settings(monkeypatch, AGENT_CHECKPOINT_URL=url)

    class FakePostgres:  # no database in unit tests; the real one is in test_postgres_checkpointer
        kind = "postgres"
        saver = InMemorySaver()

        @staticmethod
        def ok() -> bool:
            return True

        @staticmethod
        def close() -> None:
            pass

    monkeypatch.setattr("app.main.open_checkpointer", lambda _: FakePostgres)
    with TestClient(create_app(settings)) as c:
        response = c.get("/health")

    body = response.json()
    assert (body["checkpointer"], body["checkpointer_ok"]) == ("postgres", True)
    for leak in (password, "db.test", "campusspace_agent", "postgresql://"):
        assert leak not in response.text


def test_an_unreachable_checkpoint_database_fails_startup_without_leaking_the_url(
    monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture
) -> None:
    password = "sentinel-db-password-" + "p" * 16
    # Port 1 on localhost refuses at once: a real psycopg pool against nothing.
    url = f"postgresql://campusspace_agent:{password}@127.0.0.1:1/agent"
    settings = make_settings(monkeypatch, AGENT_CHECKPOINT_URL=url)
    monkeypatch.setattr("app.checkpoint.POOL_OPEN_TIMEOUT_S", 1.0)
    caplog.set_level(logging.DEBUG)

    with pytest.raises(CheckpointUnavailable) as exc, TestClient(create_app(settings)):
        pass

    assert str(exc.value) == (
        "Checkpoint database unreachable (PoolTimeout); check AGENT_CHECKPOINT_URL"
    )
    assert exc.value.__cause__ is None and exc.value.__suppress_context__
    for leak in (password, url):
        assert leak not in str(exc.value)
        assert leak not in caplog.text
