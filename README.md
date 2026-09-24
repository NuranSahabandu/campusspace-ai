# CampusSpace AI

Campus room and equipment booking. A LangGraph multi-agent service drafts proposals, Facilities Officers approve them, and .NET books them in one transaction. SE3090 group project.

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 8.0.423 (pinned by `global.json`) |
| Node | 24 |
| Flutter | 3.47 stable (Android only) |
| Python | 3.11 via [uv](https://docs.astral.sh/uv/) |
| Docker Desktop | runs PostgreSQL 16 locally |

Local ports: API `5080`, agent service `8000`, React `5173`, PostgreSQL `5432`.

## Start the database

1. Create your local env file:
   ```bash
   cp .env.example .env
   ```
2. Replace every `change-me` in `.env`. Generate each secret (`POSTGRES_PASSWORD`, `Jwt__Key`,
   `AgentService__ServiceKey`, `AgentTools__Key`) with:
   ```bash
   openssl rand -hex 32
   ```
   Use hex, not base64. The password goes into `postgresql://` URLs, where `/ + =` would need escaping.
   Put the same password in `ConnectionStrings__Default`.
3. Start PostgreSQL:
   ```bash
   docker compose up -d
   docker compose ps          # wait until STATUS shows (healthy)
   ```
4. Check the connection:
   ```bash
   docker compose exec db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select version();"'
   ```
5. Stop or reset:
   ```bash
   docker compose down        # stop and keep data
   docker compose down -v     # stop and delete all data (named volume pgdata)
   ```

`.env` is git-ignored. Never commit it. Locally, the API reads `ConnectionStrings__Default` and `Jwt__*` from
`dotnet user-secrets`. The `.env` names document those keys, and they also work as environment variables.

## Run the API

1. Start the database (see above) and wait for `(healthy)`.
2. Copy the API secrets from `.env` into `dotnet user-secrets`. The script never prints values and is safe to re-run:
   ```bash
   ./scripts/dev-secrets.sh
   ```
3. Run the API. In Development it applies pending migrations at startup:
   ```bash
   dotnet run --project backend/CampusSpace.Api
   ```
   - Health: <http://localhost:5080/health>
   - Swagger: <http://localhost:5080/swagger>
4. Run the tests. Docker must be running; Testcontainers starts its own `postgres:16`:
   ```bash
   dotnet test backend
   ```
