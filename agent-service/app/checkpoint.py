"""The durable checkpointer (plan §10.9, Task 6.D2). Never InMemorySaver outside tests.

Postgres (AGENT_CHECKPOINT_URL) is the checkpointer everywhere except a development run without
that URL, which falls back to SqliteSaver (Settings refuses the fallback outside
AGENT_ENV=development). Render's disk is wiped on every deploy, so a SQLite file there would lose
every approval paused at the human gate.

SqliteSaver serialises access with its own threading.Lock, so one connection opened with
check_same_thread=False is safe for FastAPI's background threads (langgraph-checkpoint-sqlite 3.x
documents this). PostgresSaver takes a lock too and borrows a pooled connection per call.

The URL carries the agent role's password: it is never logged, returned or put in an exception.
Errors here are fixed texts plus the exception type only.
"""

import logging
import sqlite3
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from typing import Literal

import psycopg
from langgraph.checkpoint.base import BaseCheckpointSaver
from langgraph.checkpoint.postgres import PostgresSaver
from langgraph.checkpoint.sqlite import SqliteSaver
from psycopg.rows import dict_row
from psycopg_pool import ConnectionPool, PoolTimeout

from app.config import Settings

log = logging.getLogger("agent_service.checkpoint")

CheckpointerKind = Literal["postgres", "sqlite"]

# Pool sizing (Task 6.D2). min_size 0: no connection is held open permanently, so Neon can
# auto-suspend an idle database; the next call reconnects (about a second of cold start, accepted).
# max_idle 240 s closes an idle connection before Neon's 5-minute suspend would drop it under us.
# check_connection runs on every checkout, so a connection that Neon or a restart dropped is
# replaced instead of failing the call.
POOL_MAX_SIZE = 5
POOL_MAX_IDLE_S = 240.0
POOL_OPEN_TIMEOUT_S = 10.0
POOL_CONNECT_TIMEOUT_S = 10
HEALTH_TIMEOUT_S = 2.0


class CheckpointUnavailable(RuntimeError):
    """Startup could not reach or prepare the checkpoint database. The message is a fixed text."""


@dataclass
class Checkpointer:
    kind: CheckpointerKind
    saver: BaseCheckpointSaver
    ok: Callable[[], bool]
    close: Callable[[], None]


def open_checkpointer(settings: Settings) -> Checkpointer:
    url = settings.checkpoint_url
    if url is not None:
        return open_postgres(url.get_secret_value(), open_timeout_s=POOL_OPEN_TIMEOUT_S)
    return open_sqlite(settings.checkpoint_path)


def open_sqlite(path: Path) -> Checkpointer:
    path.parent.mkdir(parents=True, exist_ok=True)
    saver = SqliteSaver(sqlite3.connect(path, check_same_thread=False))
    saver.setup()

    def ok() -> bool:
        try:
            with saver.lock:
                saver.conn.execute("SELECT 1").fetchone()
            return True
        except sqlite3.Error:
            return False

    return Checkpointer("sqlite", saver, ok, saver.conn.close)


def open_postgres(url: str, *, open_timeout_s: float) -> Checkpointer:
    """A pooled PostgresSaver; setup() (idempotent migrations) runs once, here at startup."""
    pool = ConnectionPool(
        conninfo=url,
        min_size=0,
        max_size=POOL_MAX_SIZE,
        max_idle=POOL_MAX_IDLE_S,
        check=ConnectionPool.check_connection,
        # PostgresSaver needs autocommit and dict rows. prepare_threshold=None: no server-side
        # prepared statements, which a pgbouncer in transaction mode (Neon's pooled endpoint)
        # can't keep across transactions.
        kwargs={
            "autocommit": True,
            "row_factory": dict_row,
            "prepare_threshold": None,
            "connect_timeout": POOL_CONNECT_TIMEOUT_S,
        },
        name="agent-checkpoints",
        open=False,
    )
    try:
        pool.open(wait=True, timeout=open_timeout_s)
        saver = PostgresSaver(pool)
        # min_size 0 opens nothing above, so setup() is also the startup reachability check.
        with pool.connection(timeout=open_timeout_s):
            pass
        saver.setup()
    except (psycopg.Error, PoolTimeout, OSError) as exc:
        pool.close()
        # Never str(exc): psycopg's text names the host and user. Type only, like llm_error_reason.
        raise CheckpointUnavailable(
            f"Checkpoint database unreachable ({type(exc).__name__}); check AGENT_CHECKPOINT_URL"
        ) from None

    def ok() -> bool:
        try:
            with pool.connection(timeout=HEALTH_TIMEOUT_S) as conn:
                conn.execute("SELECT 1").fetchone()
            return True
        except (psycopg.Error, PoolTimeout, OSError):
            return False

    return Checkpointer("postgres", saver, ok, pool.close)
