# agent-service

The CampusSpace AI agent service is Python 3.11 (uv), FastAPI and LangGraph. Member 3 (C) owns the scaffolding.
Only the ASP.NET Core API calls this service, and always with `X-Service-Key` (plan §7.1 rule 2). The service has no
credentials for the business database. Its tools read `/internal/agent-tools/...` with a separate `X-Agent-Key`. Its
only database login is for its own LangGraph checkpoints (Task 6.D2; see "Checkpointer").

Phase 3 status: the whole workflow runs with **deterministic stub agents** that call the real tools. Phase 4 swaps
each stub for a Gemini worker through `WORKERS` / `run_worker` and the supervisor's `stub_planner`.

## Layout (§7.2)

```
app/main.py               create_app(): FastAPI app, GET /health, lifespan builds tools, checkpointer, graph, runner
app/config.py             Settings (pydantic-settings), reads <repo>/.env anchored to this file
app/limits.py             MAX_DELEGATIONS (derived), MAX_REPLANS, recursion limits, 10 s tool / 180 s run timeouts
app/schemas.py            Pydantic contracts: agent outputs (extra="forbid"), tool args, trace rows, /workflows API
app/guardrails.py         wrap_notes(): strips <requester_notes> tags from user text, then wraps it; plain_text()
app/tools.py              @tool functions over /internal/agent-tools (X-Agent-Key), TOOL_ERROR observations, ToolRecorder
app/workers/              stub + LLM planner and venue / equipment / policy_cost workers (tool_agent.py loop), run_worker
app/validation.py         V01–V12 as pure functions (policy snapshot + injected clock) and the re-query driver
app/graph.py              State, nodes, routing, build_graph() (plan App. A.1) with interrupt() in human_gate
app/checkpoint.py         open_checkpointer(): pooled PostgresSaver, or the development SqliteSaver fallback
app/runner.py             background _run (stream_mode="updates"), timeouts, status projection for GET
app/routers/workflows.py  /workflows routes, all guarded by require_service_key
tests/                    pytest over a fake of the .NET tool routes; never reads the real .env
```

## Configuration

Settings come from the repo-root `.env`, and real environment variables override it. The names match the .NET API's names.

| Variable | Field | Required | Default |
|----------|-------|----------|---------|
| `AgentService__ServiceKey` | `service_key` | yes, ≥ 32 chars | none |
| `AgentTools__Key` | `agent_tools_key` | yes, ≥ 32 chars, ≠ service key | none |
| `API_BASE_URL` | `api_base_url` | no | `http://localhost:5080` |
| `AGENT_CHECKPOINT_URL` | `checkpoint_url` | yes, unless `AGENT_ENV=development` (secret) | none |
| `AGENT_ENV` | `agent_env` | no (`development` locally only) | `production` |
| `AGENT_CHECKPOINT_PATH` | `checkpoint_path` | no (development fallback only) | `agent-service/data/checkpoints.sqlite` (git-ignored) |
| `GOOGLE_API_KEY` | `google_api_key` | not yet (Phase 4) | none |

## Checkpointer

Paused approvals live in the LangGraph checkpointer, so it must survive a restart (plan §10.9). Render's disk is wiped
on every deploy, so the service uses **PostgresSaver** at `AGENT_CHECKPOINT_URL` (psycopg 3 pool: autocommit, dict
rows, no prepared statements for pgbouncer, connections checked on checkout, `min_size=0` so Neon can auto-suspend).
`setup()` runs once at startup. Without the URL only `AGENT_ENV=development` falls back to the SQLite file; anything
else refuses to start.

The URL is for the role `campusspace_agent`, which owns only the database `campusspace_agent` (schema
`agent_checkpoints`) and can't connect to the business database. It is a narrow, deliberate deviation from plan §7.1
rule 3 ("no database credentials"), which §10.9 anticipates. Locally:

```bash
# .env: AGENT_DB_PASSWORD=<openssl rand -hex 16 or -hex 32>
docker compose up -d
./scripts/dev-agent-db.sh          # role + database + schema (idempotent); fills AGENT_CHECKPOINT_URL when empty
uv run uvicorn app.main:app --reload --port 8000
curl -s localhost:8000/health      # "checkpointer": "postgres", "checkpointer_ok": true
```

Switching checkpointers loses the old store's threads. A run that is still Running fails "Agent run not found (agent
service state lost)". An approve of a run waiting on the old store goes through the approval-failure path, so a new
proposal is prepared (evidence D15).

The Postgres tests run against a throwaway database, never the dev one:

```bash
docker run -d --rm --name cp-test -e POSTGRES_PASSWORD=postgres -p 55432:5432 postgres:16
AGENT_TEST_POSTGRES_URL=postgresql://postgres:postgres@localhost:55432/postgres uv run pytest -q -m postgres
docker stop cp-test
```

## Run and test

```bash
uv sync
uv run uvicorn app.main:app --reload --port 8000
uv run ruff check .
uv run pytest -q                                   # no network, no LLM, no .env
LIVE_API_BASE_URL=http://localhost:5080 LIVE_AGENT_TOOLS_KEY=... LIVE_REQUEST_ID=3 uv run pytest -m live
```

## Endpoints

| Method | Path | Auth | Behaviour |
|--------|------|------|-----------|
| GET | `/health` | none | Service, stub/model config, `checkpointer: "postgres" \| "sqlite"`, `checkpointer_ok`. No model call, no secrets, no URL. |
| POST | `/workflows` | `X-Service-Key` | `{thread_id: uuid, request_id: int}` → 202 `{thread_id, status: "running"}`. 400 bad body, 409 existing thread. |
| GET | `/workflows/{thread_id}` | `X-Service-Key` | Status and trace (below). 404 unknown thread, 400 bad uuid. |
| POST | `/workflows/{thread_id}/resume` | `X-Service-Key` | `{decision: approve\|reject\|revise\|cancel, notes}` → 202. 409 unless awaiting_approval; 400 revise without notes. |

A missing or wrong key is always a 401, checked before the body. Validation errors are 400 (not FastAPI's 422).

`GET /workflows/{thread_id}` returns `thread_id, status, revision, interrupt, plan, proposal, officer_summary,
validation, nodes, steps, policy_snapshot, error, model, started_at, completed_at, duration_ms`.

- `status`: `running | awaiting_approval | completed | rejected | failed | cancelled`. A checkpoint with pending nodes
  and no live run (the service restarted mid-run) reads as `failed` with "Agent service restarted during the run".
- `validation`: every attempt, `[{attempt, rule, passed, message}]`. The latest checklist is the highest attempt.
- `steps`: `[{sequence, agent_name, status: Succeeded|Failed, input, output, retries, error, duration_ms,
  tool_calls: [{tool_name, args, result_summary, succeeded, error, duration_ms}]}]`. These map 1:1 onto AgentSteps and
  AgentToolCalls. `sequence` and `attempt` never restart within a thread, including across revisions.
- Money is a 2-dp decimal **string** (`"5500.00"`), so no float ever touches a price.
