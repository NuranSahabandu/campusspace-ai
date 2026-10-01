"""Wall-clock deadlines for LLM calls (planner and LLM workers)."""

import threading
from collections.abc import Callable
from concurrent.futures import Future
from typing import Any


def submit(fn: Callable[[], Any], name: str = "llm-call") -> Future:
    """Run fn in a daemon thread. A call that outlives the deadline is abandoned: its result is
    never read, and a daemon thread never blocks shutdown. The caller waits with
    concurrent.futures.wait(), never result(timeout=): a TimeoutError raised BY the call (3.11:
    concurrent.futures.TimeoutError is the builtin) must not read as the deadline."""
    future: Future = Future()

    def run() -> None:
        if not future.set_running_or_notify_cancel():
            return
        try:
            future.set_result(fn())
        except BaseException as exc:  # noqa: BLE001 - handed to the waiting caller
            future.set_exception(exc)

    threading.Thread(target=run, name=name, daemon=True).start()
    return future
