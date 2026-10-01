# syntax=docker/dockerfile:1
# CampusSpace agent service (Task 6.D3). Build context: agent-service/ (render.yaml: dockerContext ./agent-service).
#   docker build -t campusspace-agent agent-service
# No secret is baked in: keys, GOOGLE_API_KEY and AGENT_CHECKPOINT_URL are environment variables on the host.
# app/config.py also reads <repo root>/.env, which does not exist in the image, so only real env vars apply.

FROM python:3.11-slim-bookworm AS build
# The same uv as development and CI, pinned.
COPY --from=ghcr.io/astral-sh/uv:0.11.19 /uv /usr/local/bin/uv
ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PYTHON_DOWNLOADS=never \
    UV_PYTHON=/usr/local/bin/python3.11
WORKDIR /srv/agent-service
# Dependencies first (from uv.lock, exactly as CI: --locked), so a code change reuses this layer.
COPY pyproject.toml uv.lock ./
RUN uv sync --locked --no-dev --no-install-project
COPY app ./app

FROM python:3.11-slim-bookworm
WORKDIR /srv/agent-service
COPY --from=build /srv/agent-service /srv/agent-service
# Non-root. data/ is only the development SQLite fallback (AGENT_ENV=development); Production uses PostgreSQL.
RUN useradd --system --uid 10001 --no-create-home --shell /usr/sbin/nologin agent \
    && mkdir -p data && chown agent data
USER agent
ENV PATH=/srv/agent-service/.venv/bin:$PATH \
    PYTHONUNBUFFERED=1 \
    PORT=8000
EXPOSE 8000
# Render sets PORT. One worker: the graph runner keeps in-process state per run segment.
# app/logging.json: the service's own INFO lines (e.g. "checkpointer=postgres") reach Render's log; httpx, psycopg and
# google_genai stay at WARNING (no request URLs or connection details in the log).
CMD ["sh", "-c", "exec uvicorn app.main:app --host 0.0.0.0 --port \"$PORT\" --log-config app/logging.json"]
