"""Workflow endpoints (plan §10.12). Every route requires X-Service-Key."""

from fastapi import APIRouter, Depends, status
from fastapi.responses import JSONResponse

from app.security import require_service_key

router = APIRouter(tags=["workflows"], dependencies=[Depends(require_service_key)])


@router.post("/workflows")
def start_workflow() -> JSONResponse:
    return JSONResponse(
        status_code=status.HTTP_501_NOT_IMPLEMENTED,
        content={"detail": "Workflow engine arrives in Phase 3"},
    )
