from collections.abc import Iterator

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app

TEST_SERVICE_KEY = "t" * 64

ENV_NAMES = ("AgentService__ServiceKey", "AgentTools__Key", "GOOGLE_API_KEY", "API_BASE_URL")


@pytest.fixture(autouse=True)
def clean_env(monkeypatch: pytest.MonkeyPatch) -> None:
    """Start every test from a known env; values from the developer's shell never leak in."""
    for name in ENV_NAMES:
        monkeypatch.delenv(name, raising=False)


def make_settings(monkeypatch: pytest.MonkeyPatch, **env: str) -> Settings:
    """Settings from the given env only (_env_file=None: the real .env is never read)."""
    monkeypatch.setenv("AgentService__ServiceKey", TEST_SERVICE_KEY)
    for name, value in env.items():
        monkeypatch.setenv(name, value)
    return Settings(_env_file=None)


@pytest.fixture
def client(monkeypatch: pytest.MonkeyPatch) -> Iterator[TestClient]:
    with TestClient(create_app(make_settings(monkeypatch))) as c:
        yield c
