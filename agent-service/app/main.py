"""FastAPI entry point: uv run uvicorn app.main:app --reload --port 8000"""

import platform
from collections.abc import AsyncIterator, Callable
from contextlib import asynccontextmanager
from datetime import UTC, datetime

import httpx
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from app import __version__
from app.checkpoint import checkpointer_ok, open_checkpointer
from app.config import LLM_AGENTS, Settings, get_settings
from app.graph import build_graph
from app.routers import workflows
from app.runner import WorkflowRunner
from app.tools import ToolClient, build_tools

SERVICE_NAME = "agent-service"


def _utc_now() -> datetime:
    return datetime.now(UTC)


def create_app(
    settings: Settings | None = None,
    *,
    transport: httpx.BaseTransport | None = None,
    clock: Callable[[], datetime] = _utc_now,
) -> FastAPI:
    """transport and clock exist for tests (a fake .NET API and a fixed "now")."""

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        # Loading settings here makes a missing or short key fail startup.
        s = settings or get_settings()
        client = ToolClient(s.api_base_url, s.agent_tools_key, transport=transport)
        saver = open_checkpointer(s.checkpoint_path)
        graph = build_graph(saver, build_tools(client), clock)
        app.state.settings = s
        app.state.checkpointer = saver
        app.state.runner = WorkflowRunner(graph, clock, s.model_label())
        try:
            yield
        finally:
            client.close()
            saver.conn.close()

    app = FastAPI(title="CampusSpace AI Agent Service", version=__version__, lifespan=lifespan)

    @app.exception_handler(RequestValidationError)
    async def bad_request(_: Request, exc: RequestValidationError) -> JSONResponse:
        # 400 like the .NET API (not FastAPI's 422); messages only, never the input values.
        errors = [{"loc": list(e["loc"]), "msg": e["msg"], "type": e["type"]} for e in exc.errors()]
        return JSONResponse(status_code=400, content={"detail": errors})

    @app.get("/health")
    def health(request: Request) -> dict:
        """Liveness and config summary. No model call and no secret values (plan §10.12)."""
        s: Settings = request.app.state.settings
        return {
            "status": "ok",
            "service": SERVICE_NAME,
            "version": __version__,
            "python": platform.python_version(),
            "agents": {name: s.agent_mode(name) for name in LLM_AGENTS},
            "models": {"planner": s.planner_model, "worker": s.worker_model},
            "checkpointer": "sqlite",
            "checkpointer_ok": checkpointer_ok(request.app.state.checkpointer),
            "google_api_key_configured": s.google_api_key is not None,
        }

    app.include_router(workflows.router)
    return app


app = create_app()
