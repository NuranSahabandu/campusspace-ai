"""The Postgres checkpointer against a real PostgreSQL (Task 6.D2).

Skipped unless AGENT_TEST_POSTGRES_URL is set: CI's agent job runs a postgres:16 service for it, and
locally a throwaway container does (never the dev database). Each test gets its own schema, dropped
afterwards, so PostgresSaver.setup() creates fresh tables every time.
"""

import logging
import os
from collections.abc import Iterator
from urllib.parse import quote, urlsplit, urlunsplit
from uuid import uuid4

import psycopg
import pytest

from app.checkpoint import CheckpointUnavailable, open_postgres
from tests.fake_api import FakeCampusApi
from tests.harness import HAPPY_NODES, Harness

pytestmark = pytest.mark.postgres

ADMIN_URL = os.environ.get("AGENT_TEST_POSTGRES_URL", "")
if not ADMIN_URL:
    pytest.skip("AGENT_TEST_POSTGRES_URL is not set", allow_module_level=True)

OPEN_TIMEOUT_S = 5.0


def _with_query(url: str, **params: str) -> str:
    parts = urlsplit(url)
    query = "&".join(f"{k}={quote(v, safe='')}" for k, v in params.items())
    return urlunsplit(parts._replace(query=query))


@pytest.fixture
def schema_url() -> Iterator[tuple[str, str]]:
    """A URL whose search_path is a fresh schema, and the application_name its connections carry."""
    schema = f"cp_test_{uuid4().hex[:12]}"
    app_name = f"agent-test-{schema}"
    with psycopg.connect(ADMIN_URL, autocommit=True) as admin:
        admin.execute(f'CREATE SCHEMA "{schema}"')
    try:
        yield (
            _with_query(ADMIN_URL, options=f"-c search_path={schema}", application_name=app_name),
            app_name,
        )
    finally:
        with psycopg.connect(ADMIN_URL, autocommit=True) as admin:
            admin.execute(f'DROP SCHEMA "{schema}" CASCADE')


def _terminate(app_name: str) -> int:
    with psycopg.connect(ADMIN_URL, autocommit=True) as admin:
        rows = admin.execute(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = %s",
            (app_name,),
        ).fetchall()
    return len(rows)


def test_a_paused_run_survives_a_restart_and_finalizes(schema_url: tuple[str, str]) -> None:
    url, _ = schema_url
    api = FakeCampusApi()
    before = Harness(open_postgres(url, open_timeout_s=OPEN_TIMEOUT_S), api)
    thread_id = before.start()
    assert before.view(thread_id).status == "awaiting_approval"
    before.close()  # the process "stops": pool, graph and in-process state are gone

    after = Harness(open_postgres(url, open_timeout_s=OPEN_TIMEOUT_S), api)
    try:
        assert after.checkpointer.kind == "postgres"
        paused = after.view(thread_id)
        assert paused.status == "awaiting_approval"
        assert paused.nodes == HAPPY_NODES
        assert paused.interrupt["quote"]["total"] == "5500.00"
        policy_calls = len(api.calls_to("policy"))

        done = after.resume(thread_id, "approve")

        assert done.status == "completed"
        assert done.nodes == HAPPY_NODES + ["finalize"]
        assert len(api.calls_to("policy")) == policy_calls  # snapshot kept, never re-fetched
        assert done.policy_snapshot == paused.policy_snapshot
    finally:
        after.close()


def test_setup_is_idempotent(schema_url: tuple[str, str]) -> None:
    url, _ = schema_url
    first = open_postgres(url, open_timeout_s=OPEN_TIMEOUT_S)
    try:
        first.saver.setup()  # a second setup() on the same tables (startup already ran one)
        assert first.ok()
    finally:
        first.close()
    second = open_postgres(url, open_timeout_s=OPEN_TIMEOUT_S)  # a restart runs it again
    try:
        assert second.ok()
    finally:
        second.close()


def test_a_dropped_connection_is_replaced(schema_url: tuple[str, str]) -> None:
    """Neon's autosuspend or a database restart drops pooled connections; check_connection
    replaces them on the next checkout instead of failing the call."""
    url, app_name = schema_url
    harness = Harness(open_postgres(url, open_timeout_s=OPEN_TIMEOUT_S), FakeCampusApi())
    try:
        thread_id = harness.start()
        assert harness.view(thread_id).status == "awaiting_approval"
        assert _terminate(app_name) >= 1  # the pool keeps an idle connection after the run

        assert harness.checkpointer.ok()
        done = harness.resume(thread_id, "approve")
        assert done.status == "completed"
    finally:
        harness.close()


def test_a_wrong_password_never_reaches_the_error_or_the_logs(
    caplog: pytest.LogCaptureFixture,
) -> None:
    parts = urlsplit(ADMIN_URL)
    wrong = "wrong-sentinel-" + uuid4().hex
    netloc = f"{parts.username}:{wrong}@{parts.hostname}:{parts.port or 5432}"
    url = urlunsplit(parts._replace(netloc=netloc))
    caplog.set_level(logging.DEBUG)

    with pytest.raises(CheckpointUnavailable) as exc:
        open_postgres(url, open_timeout_s=2.0)

    assert str(exc.value) == (
        "Checkpoint database unreachable (PoolTimeout); check AGENT_CHECKPOINT_URL"
    )
    for leak in (wrong, url):
        assert leak not in str(exc.value)
        assert leak not in caplog.text
