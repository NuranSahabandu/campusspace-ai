"""Optional: one run against the REAL .NET API. Skipped unless these are set in the shell:

    LIVE_API_BASE_URL=http://localhost:5080 LIVE_AGENT_TOOLS_KEY=... LIVE_REQUEST_ID=<id> \\
        uv run pytest -m live

It never reads .env. The tools are read-only, so the run books nothing (booking is .NET's job).
"""

import os
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

import pytest
from pydantic import SecretStr

from app.checkpoint import open_sqlite
from app.graph import build_graph
from app.runner import WorkflowRunner
from app.tools import ToolClient, build_tools
from app.validation import RULES

LIVE = ("LIVE_API_BASE_URL", "LIVE_AGENT_TOOLS_KEY", "LIVE_REQUEST_ID")

pytestmark = [
    pytest.mark.live,
    pytest.mark.skipif(
        not all(os.environ.get(name) for name in LIVE), reason="set LIVE_* to run against .NET"
    ),
]


def test_a_real_request_pauses_or_fails_with_a_reason(tmp_path: Path) -> None:
    client = ToolClient(
        os.environ["LIVE_API_BASE_URL"], SecretStr(os.environ["LIVE_AGENT_TOOLS_KEY"])
    )
    checkpointer = open_sqlite(tmp_path / "live.sqlite")
    clock = lambda: datetime.now(UTC)  # noqa: E731
    graph = build_graph(checkpointer.saver, build_tools(client), clock)
    runner = WorkflowRunner(graph, clock, "stub")
    thread_id = str(uuid4())
    try:
        runner.claim_start(thread_id)
        runner.run_start(thread_id, int(os.environ["LIVE_REQUEST_ID"]))
        view = runner.view(thread_id)

        assert view is not None
        assert view.status in ("awaiting_approval", "failed")
        if view.status == "failed":
            assert view.error
        else:
            assert [v["rule"] for v in view.validation[-12:]] == RULES
            runner.claim_resume(thread_id)
            runner.run_resume(thread_id, "approve", None)
            assert runner.view(thread_id).status in ("completed", "failed")
    finally:
        client.close()
        checkpointer.close()
