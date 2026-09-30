"""Worker registry and run_worker (plan §10.6).

Phase 3 workers are deterministic stubs that call the real tools. Phase 4 replaces them one at a
time with a create_agent ReAct worker (passed to build_graph as `workers`) with the same contract:
task string in, validated structured result out. An LLM worker falls back to its stub.
The LLM workers share one loop (app/workers/tool_agent.py): venue_llm.py (4.2),
equipment_llm.py (4.3) and policy_llm.py (4.4).
"""

from collections.abc import Callable, Mapping
from typing import Any, Protocol

from langchain_core.tools import BaseTool
from pydantic import ValidationError

from app.budget import UNLIMITED, LlmBudget
from app.schemas import RESULT_MODELS
from app.tools import LOCAL_TOOLS, WORKER_TOOLS, current_recorder
from app.workers.common import LlmAttempt, WorkerFailed, WorkerOutcome, WorkerUnavailable
from app.workers.equipment import equipment_allocation
from app.workers.policy_cost import policy_cost
from app.workers.venue import venue_matching

Worker = Callable[[str, Mapping[str, BaseTool]], dict[str, Any]]


class LlmWorker(Protocol):
    def run(self, task: str, tools: Mapping[str, BaseTool], budget: LlmBudget) -> LlmAttempt: ...


WORKERS: dict[str, Worker] = {
    "venue_matching": venue_matching,
    "equipment_allocation": equipment_allocation,
    "policy_cost": policy_cost,
}

__all__ = ["WORKERS", "WorkerFailed", "WorkerOutcome", "WorkerRunner", "WorkerUnavailable"]


class WorkerRunner:
    """Holds the tool set; each worker only ever sees its own allow-listed tools."""

    def __init__(
        self, tools: Mapping[str, BaseTool], llm_workers: Mapping[str, LlmWorker] | None = None
    ) -> None:
        self._tools = tools
        self._llm = dict(llm_workers or {})

    def run_worker(self, name: str, task: str, *, budget: LlmBudget = UNLIMITED) -> WorkerOutcome:
        """ONE worker, ONE task string. Returns only the validated structured result (context
        isolation: no history goes in, no tool debris comes out). The budget is a time limit, not
        context. An LLM worker's result was checked by the worker; when it falls back, the stub runs
        here and the LLM metadata (with the fallback reason) is kept."""
        # LOCAL_TOOLS (check_policy) are built by the LLM worker from its brief.
        allowed = {t: self._tools[t] for t in WORKER_TOOLS[name] if t not in LOCAL_TOOLS}
        meta = None
        if name in self._llm:
            attempt = self._llm[name].run(task, allowed, budget)
            meta = attempt.meta
            if attempt.result is not None:
                return WorkerOutcome(RESULT_MODELS[name].model_validate(attempt.result)
                                     .model_dump(mode="json"), meta)  # fmt: skip
        return WorkerOutcome(self._run_stub(name, task, allowed), meta)

    def _run_stub(self, name: str, task: str, allowed: Mapping[str, BaseTool]) -> dict[str, Any]:
        """Invalid output is retried once (V01), then the step fails."""
        worker = WORKERS[name]
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
