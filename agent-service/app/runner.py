"""Runs the graph in the background and projects checkpoints onto the /workflows view.

_run follows Lab 06's _run(): stream_mode="updates", recording progress per node. Status comes from
the checkpoint (graph.get_state) plus an in-process set of threads that are running right now.
"""

import logging
import threading
import time
from collections.abc import Callable
from datetime import datetime
from typing import Any

from langgraph.errors import GraphRecursionError
from langgraph.types import Command

from app.budget import SEGMENT_DEADLINE
from app.graph import TERMINAL, initial_state, iso
from app.limits import GRAPH_RECURSION_LIMIT, RUN_TIMEOUT_S
from app.llm import add_usage
from app.schemas import WorkflowView

log = logging.getLogger("agent_service.runner")

RESTARTED = "Agent service restarted during the run"
TIMED_OUT = "Run timed out"


class Conflict(Exception):
    """409: the thread exists already, or is not waiting for a decision."""


class WorkflowRunner:
    def __init__(
        self,
        graph: Any,
        clock: Callable[[], datetime],
        model_label: str,
        run_timeout_s: float = RUN_TIMEOUT_S,
        monotonic: Callable[[], float] = time.monotonic,
    ) -> None:
        self._graph = graph
        self._clock = clock
        self._model = model_label
        self._timeout = run_timeout_s
        self._monotonic = monotonic
        self._running: set[str] = set()
        self._lock = threading.Lock()

    @staticmethod
    def _config(thread_id: str) -> dict[str, Any]:
        return {"configurable": {"thread_id": thread_id}, "recursion_limit": GRAPH_RECURSION_LIMIT}

    def _snapshot(self, thread_id: str):
        return self._graph.get_state(self._config(thread_id))

    # ---------- start / resume (the checks and the running mark are atomic) ----------

    def claim_start(self, thread_id: str) -> None:
        with self._lock:
            if thread_id in self._running or self._snapshot(thread_id).values:
                raise Conflict("A workflow with this thread_id already exists")
            self._running.add(thread_id)

    def claim_resume(self, thread_id: str) -> None:
        with self._lock:
            view = self._view_unlocked(thread_id)
            if view is None:
                raise KeyError(thread_id)
            if view.status != "awaiting_approval":
                raise Conflict(f"The workflow is {view.status}, not awaiting_approval")
            self._running.add(thread_id)

    def run_start(self, thread_id: str, request_id: int) -> None:
        self._run(initial_state(request_id, self._clock()), thread_id)

    def run_resume(self, thread_id: str, decision: str, notes: str | None) -> None:
        self._run(Command(resume={"decision": decision, "notes": notes or ""}), thread_id)

    def _run(self, inputs: Any, thread_id: str) -> None:
        config = self._config(thread_id)
        deadline = self._monotonic() + self._timeout
        # The LLM steps read the deadline to share the segment's time (app/budget.py).
        segment = config | {"configurable": config["configurable"] | {SEGMENT_DEADLINE: deadline}}
        try:
            for chunk in self._graph.stream(inputs, segment, stream_mode="updates"):
                for node in chunk:
                    log.info("thread %s: %s", thread_id, node)
                if self._monotonic() > deadline:
                    self._fail(config, TIMED_OUT)
                    return
        except GraphRecursionError:
            log.warning("thread %s hit the recursion limit", thread_id)
            self._fail(config, f"Graph recursion limit ({GRAPH_RECURSION_LIMIT}) reached")
        except Exception as exc:  # the run must end in a recorded state, never hang in "running"
            log.exception("thread %s crashed", thread_id)
            self._fail(config, f"Agent service error: {type(exc).__name__}")
        finally:
            with self._lock:
                self._running.discard(thread_id)

    def _fail(self, config: dict[str, Any], reason: str) -> None:
        """Make the checkpoint terminal, as if safe_failure had run, so the status is 'failed'."""
        self._graph.update_state(
            config,
            {
                "nodes": ["safe_failure"],
                "status_": "failed",
                "error": reason,
                "completed_at": iso(self._clock()),
            },
            as_node="safe_failure",
        )

    # ---------- the view ----------

    def view(self, thread_id: str) -> WorkflowView | None:
        with self._lock:
            return self._view_unlocked(thread_id)

    def _view_unlocked(self, thread_id: str) -> WorkflowView | None:
        snapshot = self._snapshot(thread_id)
        values: dict[str, Any] = snapshot.values or {}
        running = thread_id in self._running
        if not values and not running:
            return None

        nodes = list(values.get("nodes", []))
        error = values.get("error")
        interrupt = None
        interrupts = [i for task in snapshot.tasks for i in task.interrupts]
        if running:
            status = "running"
        elif interrupts:
            status = "awaiting_approval"
            interrupt = interrupts[0].value
            nodes.append("human_gate")  # shown while paused; persisted once when it returns
        elif values.get("status_") in TERMINAL:
            status = values["status_"]
        else:
            status, error = "failed", RESTARTED if snapshot.next else (error or RESTARTED)

        result = values.get("quote") or {}
        return WorkflowView(
            thread_id=thread_id,
            status=status,
            revision=values.get("revision", 1),
            interrupt=interrupt,
            plan=values.get("plan_model"),
            proposal=values.get("proposal"),
            officer_summary=result.get("officer_summary"),
            validation=values.get("validation", []),
            nodes=nodes,
            steps=values.get("steps", []),
            policy_snapshot=values.get("policy"),
            error=error if status in ("failed",) else None,
            model=self._model,
            usage=run_usage(values.get("steps", [])),
            started_at=values.get("started_at"),
            completed_at=values.get("completed_at") if status in TERMINAL else None,
            duration_ms=_duration_ms(values) if status in TERMINAL else None,
        )


def _duration_ms(values: dict[str, Any]) -> int | None:
    start, end = values.get("started_at"), values.get("completed_at")
    if not start or not end:
        return None
    delta = datetime.fromisoformat(end) - datetime.fromisoformat(start)
    return max(int(delta.total_seconds() * 1000), 0)


def run_usage(steps: list[dict[str, Any]]) -> dict[str, Any] | None:
    """The run total of every step's "usage" (plan §10.11: tokens per run for the report)."""
    total = None
    for step in steps:
        total = add_usage(total, (step.get("output") or {}).get("usage"))
    return total
