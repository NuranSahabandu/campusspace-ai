"""Builds the real graph (SqliteSaver on a temp file) over the fake .NET API, with a fixed clock."""

import json
from collections.abc import Callable
from datetime import datetime
from pathlib import Path
from typing import Any
from uuid import uuid4

from pydantic import SecretStr

from app.checkpoint import open_checkpointer
from app.graph import build_graph
from app.runner import WorkflowRunner
from app.schemas import WorkflowView
from app.tools import ToolClient, build_tools
from tests.conftest import TEST_TOOLS_KEY
from tests.fake_api import NOW, FakeCampusApi

HAPPY_NODES = [
    "supervisor",
    "venue_matching",
    "supervisor",
    "equipment_allocation",
    "supervisor",
    "policy_cost",
    "supervisor",
    "validate",
    "human_gate",
]


class Harness:
    def __init__(
        self,
        db: Path,
        api: FakeCampusApi | None = None,
        now: datetime = NOW,
        run_timeout_s: float = 180.0,
        monotonic: Callable[[], float] | None = None,
    ) -> None:
        self.api = api or FakeCampusApi()
        self.client = ToolClient(
            "http://api.test", SecretStr(TEST_TOOLS_KEY), transport=self.api.transport()
        )
        self.saver = open_checkpointer(db)
        self.graph = build_graph(self.saver, build_tools(self.client), lambda: now)
        kwargs: dict[str, Any] = {"run_timeout_s": run_timeout_s}
        if monotonic:
            kwargs["monotonic"] = monotonic
        self.runner = WorkflowRunner(self.graph, lambda: now, "stub", **kwargs)

    def start(self, request_id: int = 42) -> str:
        thread_id = str(uuid4())
        self.runner.claim_start(thread_id)
        self.runner.run_start(thread_id, request_id)
        return thread_id

    def resume(self, thread_id: str, decision: str, notes: str | None = None) -> WorkflowView:
        self.runner.claim_resume(thread_id)
        self.runner.run_resume(thread_id, decision, notes)
        return self.view(thread_id)

    def view(self, thread_id: str) -> WorkflowView:
        view = self.runner.view(thread_id)
        assert view is not None
        return view

    def state(self, thread_id: str) -> dict[str, Any]:
        return self.graph.get_state({"configurable": {"thread_id": thread_id}}).values

    def state_json(self, thread_id: str) -> str:
        return json.dumps(self.state(thread_id), default=str)

    def close(self) -> None:
        self.client.close()
        self.saver.conn.close()


def latest(view: WorkflowView) -> list[dict[str, Any]]:
    attempt = max(v["attempt"] for v in view.validation)
    return [v for v in view.validation if v["attempt"] == attempt]
