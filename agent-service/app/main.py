"""FastAPI entry point: uv run uvicorn app.main:app --reload --port 8000"""

import logging
import platform
from collections.abc import AsyncIterator, Callable
from contextlib import asynccontextmanager
from datetime import UTC, datetime

import httpx
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from app import __version__
from app.checkpoint import open_checkpointer
from app.config import LLM_AGENTS, Settings, get_settings
from app.graph import build_graph
from app.llm import build_chat_model
from app.routers import workflows
from app.runner import WorkflowRunner
from app.tools import ToolClient, build_tools
from app.workers.equipment_llm import LlmEquipmentWorker
from app.workers.planner import LlmPlanner
from app.workers.policy_llm import LlmPolicyWorker
from app.workers.venue_llm import LlmVenueWorker

SERVICE_NAME = "agent-service"
LIVE_PATH = "/health/live"
log = logging.getLogger("agent_service.main")


class _SkipLivenessPings(logging.Filter):
    """Drops uvicorn's access line for the liveness pings (every few seconds on Render)."""

    def filter(self, record: logging.LogRecord) -> bool:
        args = record.args
        # uvicorn.access: (client, method, path, http_version, status)
        return not (isinstance(args, tuple) and len(args) >= 3 and args[2] == LIVE_PATH)


LIVENESS_LOG_FILTER = _SkipLivenessPings()


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
        checkpointer = open_checkpointer(s)  # first: a failure here leaves nothing to close
        log.info("checkpointer=%s", checkpointer.kind)  # the kind only, never the URL
        client = ToolClient(s.api_base_url, s.agent_tools_key, transport=transport)
        planner = None
        if s.agent_mode("supervisor") == "llm":
            # Lazy: the Gemini client is built on the first plan, never at startup.
            planner = LlmPlanner(
                lambda: build_chat_model("planner", s, "supervisor"), s.planner_model
            )
        workers = {}
        for name, worker_class in (
            ("venue_matching", LlmVenueWorker),
            ("equipment_allocation", LlmEquipmentWorker),
            ("policy_cost", LlmPolicyWorker),
        ):
            if s.agent_mode(name) == "llm":  # lazy: each builds its client on first use
                workers[name] = worker_class(
                    lambda n=name: build_chat_model("worker", s, n), s.worker_model
                )
        graph = build_graph(checkpointer.saver, build_tools(client), clock, planner, workers)
        fault = s.fault_summary()
        if fault:  # names only; never in production (Settings refuses it outside development)
            times = fault["times"] or "every call"
            log.warning("FAULT INJECTION ACTIVE: fault=%s agents=%s times=%s",
                        fault["fault"], ",".join(fault["agents"]), times)  # fmt: skip
        app.state.settings = s
        app.state.checkpointer = checkpointer
        app.state.runner = WorkflowRunner(graph, clock, s.model_label())
        try:
            yield
        finally:
            client.close()
            checkpointer.close()

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
            "thinking": {"planner": s.planner_thinking, "worker": s.worker_thinking},
            # The kind and a reachability bool only: never the URL, host, user or password.
            "checkpointer": request.app.state.checkpointer.kind,
            "checkpointer_ok": request.app.state.checkpointer.ok(),
            "google_api_key_configured": s.google_api_key is not None,
            "fault_injection": s.fault_summary(),
        }

    @app.get(LIVE_PATH)
    def live() -> dict:
        """The platform's health check: no settings, no checkpointer or other outbound call, so a
        ping never wakes the database. /health (with checkpointer_ok) stays the evidence URL."""
        return {"status": "ok"}

    app.include_router(workflows.router)
    return app


logging.getLogger("uvicorn.access").addFilter(LIVENESS_LOG_FILTER)
app = create_app()
