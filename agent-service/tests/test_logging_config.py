"""app/logging.json, the logging config the Docker image starts uvicorn with (Task 6.D3)."""

import json
import logging
import logging.config
from collections.abc import Iterator
from pathlib import Path

import pytest

CONFIG = Path(__file__).resolve().parents[1] / "app" / "logging.json"


@pytest.fixture
def configured() -> Iterator[None]:
    names = ["", "uvicorn", "uvicorn.error", "uvicorn.access", "httpx", "httpcore", "psycopg",
             "google_genai", "agent_service.main"]  # fmt: skip
    saved = {n: (logging.getLogger(n).level, list(logging.getLogger(n).handlers),
                 logging.getLogger(n).propagate) for n in names}  # fmt: skip
    logging.config.dictConfig(json.loads(CONFIG.read_text()))
    yield
    for name, (level, handlers, propagate) in saved.items():
        logger = logging.getLogger(name)
        logger.setLevel(level)
        logger.handlers[:] = handlers
        logger.propagate = propagate


def test_the_services_own_info_lines_are_logged(configured: None) -> None:
    assert logging.getLogger("agent_service.main").isEnabledFor(logging.INFO)
    assert logging.getLogger("app.checkpoint").isEnabledFor(logging.INFO)


def test_library_request_and_connection_logs_stay_at_warning(configured: None) -> None:
    for name in ("httpx", "httpcore", "psycopg", "psycopg.pool", "google_genai"):
        assert not logging.getLogger(name).isEnabledFor(logging.INFO), name
        assert logging.getLogger(name).isEnabledFor(logging.WARNING), name


def test_the_dockerfile_starts_uvicorn_with_this_config() -> None:
    dockerfile = (CONFIG.parents[1] / "Dockerfile").read_text()
    assert "--log-config app/logging.json" in dockerfile
