from collections.abc import Iterator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app
from tests.fake_api import NOW, FakeCampusApi
from tests.keys import TEST_SERVICE_KEY, TEST_TOOLS_KEY

__all__ = ["TEST_SERVICE_KEY", "TEST_TOOLS_KEY", "make_settings"]

ENV_NAMES = (
    "AgentService__ServiceKey",
    "AgentTools__Key",
    "GOOGLE_API_KEY",
    "API_BASE_URL",
    "AGENT_CHECKPOINT_PATH",
    "AGENT_LLM_AGENTS",
    "PLANNER_MODEL",
    "WORKER_MODEL",
    "PLANNER_THINKING",
    "WORKER_THINKING",
    "RUN_LIVE_LLM",
)


@pytest.fixture(autouse=True)
def clean_env(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    """Start every test from a known env; values from the developer's shell never leak in.
    Every test gets its own checkpoint file, so nothing is written to agent-service/data."""
    for name in ENV_NAMES:
        monkeypatch.delenv(name, raising=False)
    monkeypatch.setenv("AGENT_CHECKPOINT_PATH", str(tmp_path / "checkpoints.sqlite"))


def make_settings(monkeypatch: pytest.MonkeyPatch, **env: str) -> Settings:
    """Settings from the given env only (_env_file=None: the real .env is never read)."""
    monkeypatch.setenv("AgentService__ServiceKey", TEST_SERVICE_KEY)
    monkeypatch.setenv("AgentTools__Key", TEST_TOOLS_KEY)
    for name, value in env.items():
        monkeypatch.setenv(name, value)
    return Settings(_env_file=None)


@pytest.fixture
def fake_api() -> FakeCampusApi:
    return FakeCampusApi()


@pytest.fixture
def client(monkeypatch: pytest.MonkeyPatch, fake_api: FakeCampusApi) -> Iterator[TestClient]:
    app = create_app(make_settings(monkeypatch), transport=fake_api.transport(), clock=lambda: NOW)
    with TestClient(app) as c:
        yield c
