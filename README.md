[![ci](https://github.com/NuranSahabandu/campusspace-ai/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/NuranSahabandu/campusspace-ai/actions/workflows/ci.yml)

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
   Put the same password in `ConnectionStrings__Default`. `Jwt__Key` must be at least 32 bytes (the hex output is 64),
   or the API refuses to start.
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

## Run the agent service

The API's `/health` includes an `agent-service` check. When the agent service is down, the overall status is
`Degraded`, but the endpoint still returns HTTP 200.

1. Install [uv](https://docs.astral.sh/uv/). It installs Python 3.11 by itself.
2. In `.env`, set `AgentService__ServiceKey` to at least 32 characters (`openssl rand -hex 32`). The service refuses to start otherwise.
   Re-run `./scripts/dev-secrets.sh` so the API has the same key.
3. Run it and check it:
   ```bash
   cd agent-service
   uv sync
   uv run uvicorn app.main:app --reload --port 8000
   curl -s localhost:8000/health
   ```
4. Lint and test. The tests set their own fake keys and never read `.env`:
   ```bash
   uv run ruff check .
   uv run pytest -q
   ```

See [agent-service/README.md](agent-service/README.md) for configuration and endpoints.

## Run the web app

The staff portal (Facilities Officers and Admins) is React 19 + Vite. It reads `VITE_API_URL` from the repo-root `.env`.

1. Start the database and the API (see above).
2. Run it:
   ```bash
   cd web
   npm ci
   npm run dev                # http://localhost:5173
   ```
3. Lint, test and build. The tests mock the API with MSW and never touch the network:
   ```bash
   npm run lint
   npm test -- --run
   npm run build
   ```

Sign in as `perera@campusspace.local` or `admin@campusspace.local` (see "Test accounts"). Students, lecturers and
technicians are refused: they use the mobile app. See [web/README.md](web/README.md) for the structure.

## Run the mobile app

The requester and technician app is Flutter (Android only). Students, lecturers and lab technicians sign in here;
Facilities Officers and Admins are sent to the web portal. The API URL is set at build time with `--dart-define`.
The Android emulator reaches your machine's `localhost` through `10.0.2.2`.

1. Start the database and the API (see above).
2. Start the emulator and run the app:
   ```bash
   cd mobile
   flutter pub get
   flutter emulators --launch Pixel_10       # or any Android emulator; `flutter devices` lists the ids
   flutter run -d <emulator-id> --dart-define=API_URL=http://10.0.2.2:5080
   ```
3. Analyze and test. The tests use fakes and never touch the network:
   ```bash
   flutter analyze
   flutter test
   ```

Debug builds allow plain HTTP only to `10.0.2.2` and `localhost`; release builds are HTTPS-only. Building a release
APK comes in Phase 6. See [mobile/README.md](mobile/README.md) for the structure.

## Authentication

Every `/api/...` route needs a JWT unless it is marked anonymous. The anonymous routes are `POST /api/auth/register`,
`POST /api/auth/login` and `/health`. Roles come from `Models/Roles.cs`.

1. Register (always creates a **Student**) or log in. Both return `{ accessToken, expiresAt, user }`:
   ```bash
   curl -s -X POST http://localhost:5080/api/auth/login -H 'Content-Type: application/json' \
     -d '{"email":"admin@campusspace.local","password":"CampusSpace#2026"}'
   ```
2. Send the token as `Authorization: Bearer <accessToken>`. Tokens last 120 minutes (`Jwt:AccessTokenMinutes`).
3. In Swagger, click **Authorize** and paste the `accessToken` without the `Bearer ` prefix.

`GET /api/auth/me` returns the signed-in user. `GET /api/users` (Admin) lists users with
`?search=&role=&sort=&page=&pageSize=`; `GET/POST/PUT /api/users/{id}` manage them. `GET /api/clubs` lists active clubs
(any role); Admins manage clubs, members and representatives. `GET /api/audit-logs` (Admin) shows every audited change
and login, filtered by `?entityType=&action=&userId=&from=&to=`. `GET /api/buildings`, `/api/features` and `/api/rooms`
(`?buildingId=&type=&minCapacity=&features=computers,projector&search=&sort=&page=&pageSize=`) work for any role;
Facilities Officers manage them and `/api/rooms/{id}/blackouts`. Errors, including 401 and 403, are Problem Details with a `traceId`.

### Test accounts

In Development, the API seeds any of these accounts whose email is missing, plus three clubs when the `Clubs` table
is empty. They are public demo credentials,
not secrets. All of them use the password **`CampusSpace#2026`** (`Seed:DemoPassword` in `appsettings.Development.json`).

| Email | Role | Name |
|-------|------|------|
| `kavindi@campusspace.local` | Student | Kavindi Perera |
| `lecturer@campusspace.local` | Lecturer | Dr. Nimal Fernando |
| `tech@campusspace.local` | LabTechnician | Sunil Jayasinghe |
| `perera@campusspace.local` | FacilitiesOfficer | Mr. Perera |
| `admin@campusspace.local` | Admin | System Admin |
| `ishan@campusspace.local` | Student | Ishan Silva |
| `nethmi@campusspace.local` | Student | Nethmi Rajapaksa |
| `tharindu@campusspace.local` | Student | Tharindu Wickramasinghe |

Clubs (representative first): **Robotics Club** (Kavindi; Ishan, Dr. Nimal), **Drama Society** (Nethmi; Tharindu),
**IEEE Student Branch** (Tharindu; Ishan, Kavindi).

Facilities: buildings **MB**, **NB**, **EB**; features `projector`, `computers`, `whiteboard`, `ac`, `sound_system`,
`smart_board`; 16 rooms of all four types (any missing room code is added). Walkthrough labs: **A301** (MB, 48, computers,
projector, ac, whiteboard), **A305** (MB, 50, no projector) and **N201** (NB, 60, with smart_board). **E305** is inactive.
One "Projector maintenance" blackout on **A101** next Monday 08:00–12:00 (campus time) is added when there are no blackouts.

To re-seed, wipe the database (`docker compose down -v`) and run the API again.
