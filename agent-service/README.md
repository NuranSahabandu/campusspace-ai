# agent-service

The CampusSpace AI agent service is Python 3.11 (uv) and FastAPI. Member 3 (C) owns the scaffolding.
Only the ASP.NET Core API calls this service, and always with `X-Service-Key` (plan §7.1 rule 2). The service has no database credentials.

Phase 0.5 status: only the skeleton exists. LangGraph and LangChain come in Phase 3, with the lab pins.

## Layout (§7.2)

```
app/main.py               create_app(): FastAPI app, GET /health, lifespan loads settings
app/config.py             Settings (pydantic-settings), reads <repo>/.env anchored to this file
app/security.py           require_service_key dependency (X-Service-Key, constant-time compare)
app/routers/workflows.py  /workflows router, every route guarded by require_service_key
tests/                    pytest; fixtures set their own env and never read the real .env
```

## Configuration

Settings come from the repo-root `.env`, and real environment variables override it. The names match the .NET API's names.

| Variable | Field | Required | Default |
|----------|-------|----------|---------|
| `AgentService__ServiceKey` | `service_key` | yes, ≥ 32 chars | none |
| `AgentTools__Key` | `agent_tools_key` | not yet (Phase 3 tools) | none |
| `GOOGLE_API_KEY` | `google_api_key` | not yet | none |
| `API_BASE_URL` | `api_base_url` | no | `http://localhost:5080` |

Model ids default to `gemini-2.5-flash` (planner) and `gemini-2.5-flash-lite` (workers). They still need to be checked
against the Labs 05–07 code (TODO in `app/config.py`).

## Run and test

```bash
uv sync
uv run uvicorn app.main:app --reload --port 8000
uv run ruff check .
uv run pytest -q
```

## Endpoints

| Method | Path | Auth | Now |
|--------|------|------|-----|
| GET | `/health` | none | `{status, service, version, python, models, checkpointer, google_api_key_configured}`. No model call and no secret values. |
| POST | `/workflows` | `X-Service-Key` | 401 if the key is missing or wrong. 501 `Workflow engine arrives in Phase 3` otherwise. |
