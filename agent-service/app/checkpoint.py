"""The durable checkpointer (plan §10.9, Lab 06 stretch goal 4). Never InMemorySaver outside tests.

SqliteSaver serialises access with its own threading.Lock, so one connection opened with
check_same_thread=False is safe for FastAPI's background threads (langgraph-checkpoint-sqlite 3.x
documents this).
"""

import sqlite3
from pathlib import Path

from langgraph.checkpoint.sqlite import SqliteSaver


def open_checkpointer(path: Path) -> SqliteSaver:
    path.parent.mkdir(parents=True, exist_ok=True)
    saver = SqliteSaver(sqlite3.connect(path, check_same_thread=False))
    saver.setup()
    return saver


def checkpointer_ok(saver: SqliteSaver) -> bool:
    try:
        with saver.lock:
            saver.conn.execute("SELECT 1").fetchone()
        return True
    except sqlite3.Error:
        return False
