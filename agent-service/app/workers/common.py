"""What every worker shares: the task-brief format and tool observations.

A task is one instruction line plus "BRIEF: {json}" with ids and values, never whole records
(plan §10.5). The stubs parse the JSON; a Phase 4 LLM worker reads the same text.
"""

import json
from collections.abc import Mapping
from dataclasses import dataclass, field
from decimal import Decimal
from typing import Any

from langchain_core.tools import BaseTool

from app.tools import error_text, is_error, is_unavailable, parse_json

BRIEF_MARKER = "\nBRIEF: "


class WorkerUnavailable(Exception):
    """A tool the worker needs is unavailable (network, timeout, 5xx): the step fails."""


class WorkerFailed(Exception):
    """The worker's output failed its schema twice (V01: retry once, then safe failure)."""


def make_task(instruction: str, brief: Mapping[str, Any]) -> str:
    return instruction + BRIEF_MARKER + json.dumps(brief, default=str, sort_keys=True)


def parse_brief(task: str) -> dict[str, Any]:
    return json.loads(task.split(BRIEF_MARKER, 1)[1], parse_float=Decimal)


def observe(
    tools: Mapping[str, BaseTool], name: str, args: dict[str, Any]
) -> tuple[Any, str | None]:
    """(parsed result, None) or (None, domain error text). Raises WorkerUnavailable when the tool
    cannot be used, because no answer is better than a guess."""
    observation = tools[name].invoke(args)
    if is_unavailable(observation):
        detail = error_text(observation).removeprefix("unavailable: ")
        raise WorkerUnavailable(f"Tool {name} unavailable: {detail}")
    if is_error(observation):
        return None, error_text(observation).removeprefix("HTTP ")
    return parse_json(observation), None


@dataclass
class WorkerOutcome:
    """What run_worker returns: the validated result, plus an LLM worker's step metadata (mode,
    model, attempts, usage, corrections, fallback_reason). meta is None for a stub."""

    result: dict[str, Any]
    meta: dict[str, Any] | None = None


@dataclass
class LlmAttempt:
    """What an LLM worker hands back to run_worker. result None means "use the stub" (fallback);
    meta is kept either way."""

    result: dict[str, Any] | None
    meta: dict[str, Any] = field(default_factory=dict)
