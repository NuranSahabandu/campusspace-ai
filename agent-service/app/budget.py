"""The shared LLM time budget of one run segment (a start, or one resume).

The runner puts the segment's monotonic deadline into the graph config; each node that may call an
LLM builds an LlmBudget from it. allow(own) is what the step may wait: its own deadline, cut so that
LLM_RESERVE_S stays for the non-LLM steps, or None (skip the LLM, use the stub) when that is below
LLM_MIN_BUDGET_S. Every LLM wait therefore ends by RUN_TIMEOUT_S - LLM_RESERVE_S.
"""

import time
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from typing import Any

from app.limits import LLM_MIN_BUDGET_S, LLM_RESERVE_S

SEGMENT_DEADLINE = "segment_deadline"  # key in config["configurable"]
BUDGET_EXHAUSTED = "run time budget exhausted"


@dataclass(frozen=True)
class LlmBudget:
    deadline: float | None  # monotonic time the segment ends; None = no segment limit
    monotonic: Callable[[], float] = time.monotonic
    reserve_s: float = LLM_RESERVE_S
    min_s: float = LLM_MIN_BUDGET_S

    def allow(self, own_s: float) -> float | None:
        """Seconds this LLM step may take, or None when the budget is exhausted."""
        if self.deadline is None:
            return own_s
        left = self.deadline - self.monotonic() - self.reserve_s
        return min(own_s, left) if left >= self.min_s else None

    @classmethod
    def from_config(
        cls, config: Mapping[str, Any] | None, monotonic: Callable[[], float]
    ) -> "LlmBudget":
        configurable = (config or {}).get("configurable") or {}
        return cls(configurable.get(SEGMENT_DEADLINE), monotonic)


UNLIMITED = LlmBudget(None)
