"""The shared create_agent worker loop (plan §10.4, §10.6, §10.10; Lab 05/07 create_agent).

A ToolAgentWorker runs one create_agent ReAct loop per attempt with ONLY its own tools, a structured
answer (response_format=ToolStrategy(schema, handle_errors=False)) and the task string as its only
input (context isolation). Code then checks the answer against what THIS attempt's tools returned:
the model chooses and explains, code owns the facts. Invalid output is retried once with a hint; two
failures, an exception, the deadline, an exhausted run budget or an unavailable tool fall back to
the stub (run_worker runs it), and the run continues.

ToolStrategy rather than the bare class: AutoStrategy would pick Gemini's native JSON mode in
production but the tool strategy for a fake model, so the tests would not run the production path.
handle_errors=False makes our retry (which includes the code checks) the only retry.

Subclasses set name, label, prompt, schema and retry_hint, and implement check(); local_tools() adds
tools bound to the brief (policy_llm.py's check_policy).
"""

import json
import logging
import threading
import time
from collections.abc import Callable, Mapping
from concurrent.futures import wait
from dataclasses import dataclass
from typing import Any

from langchain.agents import create_agent
from langchain.agents.structured_output import StructuredOutputValidationError, ToolStrategy
from langchain_core.messages import AIMessage, HumanMessage
from langchain_core.tools import BaseTool
from pydantic import BaseModel, ValidationError

from app.budget import BUDGET_EXHAUSTED, LlmBudget
from app.limits import (
    LLM_MIN_BUDGET_S,
    MAX_WORKER_ATTEMPTS,
    WORKER_DEADLINE_S,
    WORKER_RECURSION_LIMIT,
)
from app.llm import add_usage, llm_error_reason, usage_from
from app.tools import WORKER_TOOLS, ToolRecorder, current_recorder, recording
from app.workers.common import LlmAttempt, parse_brief
from app.workers.deadline import submit


@dataclass
class Checked:
    """A code check's verdict: the corrected result, every correction, and problem (None = valid,
    otherwise the reason the output is invalid and is retried)."""

    result: dict[str, Any] | None
    corrections: list[str]
    problem: str | None = None


@dataclass
class _Run:
    """Filled by the agent thread as it goes, so a timed-out attempt still shows its partial
    messages, tool calls and usage."""

    state: dict[str, Any] | None = None
    recorder: ToolRecorder | None = None


def _merge(src: ToolRecorder | None, dst: ToolRecorder | None) -> None:
    """Copy an attempt's tool calls and seen ids/codes into the step's recorder (a snapshot: calls
    an abandoned thread makes later never reach the step)."""
    if src is None or dst is None:
        return
    dst.calls.extend(list(src.calls))
    dst.seen_room_ids.extend(i for i in list(src.seen_room_ids) if i not in dst.seen_room_ids)
    dst.seen_equipment_codes.extend(
        c for c in list(src.seen_equipment_codes) if c not in dst.seen_equipment_codes
    )


def _usage(messages: list[Any], prompt: str) -> dict[str, Any] | None:
    """Every model call in the loop: one AIMessage each."""
    total, chars = None, len(prompt)
    for message in messages:
        text = (
            message.content
            if isinstance(message.content, str)
            else json.dumps(message.content, default=str)
        )
        if isinstance(message, AIMessage):
            out = len(text) + len(json.dumps(message.tool_calls, default=str))
            total = add_usage(total, usage_from(message, chars, out))
        chars += len(text)
    return total


def _schema_problem(exc: StructuredOutputValidationError, schema: str) -> str:
    """The first schema error (the source is a ValueError raised from the ValidationError)."""
    error = exc.source if isinstance(exc.source, ValidationError) else exc.source.__cause__
    if isinstance(error, ValidationError):
        first = error.errors()[0]
        return f"{schema} {'.'.join(str(p) for p in first['loc'])}: {first['msg']}"
    return f"{schema} invalid ({type(exc.source).__name__})"


class ToolAgentWorker:
    """One create_agent loop per attempt; the model and agent are built lazily on first use."""

    name: str
    label: str  # "Venue", "Equipment": the start of every fallback reason
    prompt: str
    schema: type[BaseModel]
    retry_hint: str  # formatted with {problem}
    log: logging.Logger

    def __init__(
        self,
        model_factory: Callable[[], Any],
        model_id: str,
        *,
        deadline_s: float = WORKER_DEADLINE_S,
        min_retry_s: float = LLM_MIN_BUDGET_S,
        monotonic: Callable[[], float] = time.monotonic,
    ) -> None:
        self._factory = model_factory
        self.model_id = model_id
        self._deadline_s = deadline_s
        self._min_retry_s = min_retry_s
        self._monotonic = monotonic
        self._agent: Any = None
        self._model: Any = None
        self._lock = threading.Lock()

    def check(self, answer: Any, brief: Mapping[str, Any], messages: list[Any]) -> Checked:
        raise NotImplementedError

    def skip(self, brief: Mapping[str, Any]) -> dict[str, Any] | None:
        """A result that needs no model call (for example nothing requested), or None."""
        return None

    def local_tools(self, brief: Mapping[str, Any]) -> list[BaseTool]:
        """Tools bound to this task's brief (no HTTP), for example check_policy over the run's
        policy snapshot. With any, the agent is built per run instead of once."""
        return []

    def _build(self, tools: Mapping[str, BaseTool], local: list[BaseTool]) -> Any:
        with self._lock:
            if self._agent is not None and not local:
                return self._agent
            if self._model is None:
                self._model = self._factory()
            chosen = [tools[n] for n in WORKER_TOOLS[self.name] if n in tools] + local
            # Least privilege: ONLY this worker's allow-list, in its order.
            if sorted(t.name for t in chosen) != sorted(WORKER_TOOLS[self.name]):
                raise RuntimeError(f"{self.name} tools {[t.name for t in chosen]} do not match its "
                                   "allow-list")  # fmt: skip
            by_name = {t.name: t for t in chosen}
            agent = create_agent(
                self._model,
                tools=[by_name[n] for n in WORKER_TOOLS[self.name]],
                system_prompt=self.prompt,
                response_format=ToolStrategy(self.schema, handle_errors=False),
                name=self.name,
            )
            if not local:
                self._agent = agent
            return agent

    @staticmethod
    def _invoke(agent: Any, message: str, run: _Run) -> Any:
        # A fresh thread has no recorder: bind one here. ToolNode's executor copies this context,
        # so the @tool functions record into it.
        with recording() as recorder:
            run.recorder = recorder
            for state in agent.stream(
                {"messages": [HumanMessage(content=message)]},
                {"recursion_limit": WORKER_RECURSION_LIMIT},
                stream_mode="values",
            ):
                run.state = state
        return (run.state or {}).get("structured_response")

    def run(self, task: str, tools: Mapping[str, BaseTool], budget: LlmBudget) -> LlmAttempt:
        started = self._monotonic()
        step = current_recorder()
        label, schema = self.label, self.schema.__name__
        meta: dict[str, Any] = {
            "mode": "llm",
            "model": self.model_id,
            "attempts": 0,
            "usage": None,
            "corrections": [],
            "worker_fallback": False,
        }

        def fallback(reason: str) -> LlmAttempt:
            self.log.warning("%s worker fell back after %d attempt(s): %s", self.name,
                             meta["attempts"], reason)  # fmt: skip
            meta.update(mode="fallback", worker_fallback=True, fallback_reason=reason)
            return LlmAttempt(None, meta)

        brief = parse_brief(task)
        skipped = self.skip(brief)
        if skipped is not None:
            return LlmAttempt(skipped, meta)
        limit = budget.allow(self._deadline_s)
        if limit is None:
            return fallback(BUDGET_EXHAUSTED)
        try:
            agent = self._build(tools, self.local_tools(brief))
        except Exception as exc:  # noqa: BLE001 - a broken client must not stop the run
            return fallback(f"{label} LLM error: {llm_error_reason(exc)}")

        problem = f"no {schema} was returned"
        for attempt in range(MAX_WORKER_ATTEMPTS):
            remaining = limit - (self._monotonic() - started)
            if attempt > 0:
                if remaining < self._min_retry_s:
                    return fallback(f"{label} output invalid; no time left to retry ({problem})")
                if step is not None:
                    step.retries += 1
            meta["attempts"] += 1
            message = task if attempt == 0 else task + self.retry_hint.format(problem=problem)
            run = _Run()
            future = submit(lambda m=message, r=run: self._invoke(agent, m, r), f"{self.name}-llm")
            wait([future], timeout=max(remaining, 0.0))
            messages = list((run.state or {}).get("messages") or [])
            _merge(run.recorder, step)
            if not future.done():
                meta["usage"] = add_usage(meta["usage"], _usage(messages, self.prompt))
                return fallback(f"{label} LLM timed out after {limit:.3g} s")
            try:
                answer = future.result()
            except StructuredOutputValidationError as exc:
                meta["usage"] = add_usage(
                    meta["usage"], _usage(messages + [exc.ai_message], self.prompt)
                )
                problem = _schema_problem(exc, schema)
                continue
            except Exception as exc:  # noqa: BLE001 - API errors, recursion limit, 429s
                meta["usage"] = add_usage(meta["usage"], _usage(messages, self.prompt))
                return fallback(f"{label} LLM error: {llm_error_reason(exc)}")
            meta["usage"] = add_usage(meta["usage"], _usage(messages, self.prompt))
            unavailable = run.recorder.unavailable_errors() if run.recorder else []
            if unavailable:
                return fallback(unavailable[0])
            if answer is None:
                problem = f"no {schema} was returned"
                continue
            checked = self.check(answer, brief, messages)
            meta["corrections"] += checked.corrections
            if checked.problem is None:
                self.log.info("%s: llm result after %d attempt(s), usage %s", self.name,
                              meta["attempts"], meta["usage"])  # fmt: skip
                return LlmAttempt(checked.result, meta)
            problem = checked.problem
        return fallback(f"{label} output invalid twice: {problem}")
