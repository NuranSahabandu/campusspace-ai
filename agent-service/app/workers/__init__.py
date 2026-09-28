"""Worker registry and run_worker (plan §10.6).

Phase 3 workers are deterministic stubs that call the real tools. Phase 4 swaps one entry of
WORKERS at a time for a create_agent ReAct worker with the same signature: task string in,
structured result out.
"""

from collections.abc import Callable, Mapping
from typing import Any

from langchain_core.tools import BaseTool
from pydantic import ValidationError

from app.schemas import RESULT_MODELS
from app.tools import WORKER_TOOLS, current_recorder
from app.workers.common import WorkerFailed, WorkerUnavailable
from app.workers.equipment import equipment_allocation
from app.workers.policy_cost import policy_cost
from app.workers.venue import venue_matching

Worker = Callable[[str, Mapping[str, BaseTool]], dict[str, Any]]

WORKERS: dict[str, Worker] = {
    "venue_matching": venue_matching,
    "equipment_allocation": equipment_allocation,
    "policy_cost": policy_cost,
}

__all__ = ["WORKERS", "WorkerFailed", "WorkerRunner", "WorkerUnavailable"]


class WorkerRunner:
    """Holds the tool set; each worker only ever sees its own allow-listed tools."""

    def __init__(self, tools: Mapping[str, BaseTool]) -> None:
        self._tools = tools

    def run_worker(self, name: str, task: str) -> dict[str, Any]:
        """ONE worker, ONE task string. Returns only the validated structured result (context
        isolation: no history goes in, no tool debris comes out). Invalid output is retried once
        (V01), then the step fails."""
        worker = WORKERS[name]
        allowed = {t: self._tools[t] for t in WORKER_TOOLS[name]}
        model = RESULT_MODELS[name]
        for attempt in range(2):
            raw = worker(task, allowed)
            try:
                return model.model_validate(raw).model_dump(mode="json")
            except ValidationError as exc:
                error = exc.errors()[0]
                reason = f"{'.'.join(str(p) for p in error['loc'])}: {error['msg']}"
                recorder = current_recorder()
                if attempt == 0 and recorder is not None:
                    recorder.retries += 1
        raise WorkerFailed(f"V01: {name} output does not match its schema ({reason})")
