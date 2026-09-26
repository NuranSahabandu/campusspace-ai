"""FastAPI entry point: uv run uvicorn app.main:app --reload --port 8000"""

import platform
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request

from app import __version__
from app.config import Settings, get_settings
from app.routers import workflows

SERVICE_NAME = "agent-service"


def create_app(settings: Settings | None = None) -> FastAPI:
    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        # Loading settings here makes a missing or short service key fail startup.
        app.state.settings = settings or get_settings()
        yield

    app = FastAPI(title="CampusSpace AI Agent Service", version=__version__, lifespan=lifespan)

    @app.get("/health")
    def health(request: Request) -> dict:
        """Liveness and config summary. No model call and no secret values (plan §10.12)."""
        s: Settings = request.app.state.settings
        return {
            "status": "ok",
            "service": SERVICE_NAME,
            "version": __version__,
            "python": platform.python_version(),
            "models": {"planner": s.planner_model, "worker": s.worker_model},
            "checkpointer": "not_configured",
            "google_api_key_configured": s.google_api_key is not None,
        }

    app.include_router(workflows.router)
    return app


app = create_app()
