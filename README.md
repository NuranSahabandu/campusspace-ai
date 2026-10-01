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

### Email (Brevo)

Approvals, rejections, automatic closes, re-plans and officer cancellations email the requester (plan §14); an approval
also carries a `booking.ics`. Set `Email__BrevoApiKey` and `Email__FromAddress` (a sender verified in Brevo) in `.env`,
plus `Email__RedirectAllTo` to send every email to one inbox while testing, then re-run `./scripts/dev-secrets.sh`.
Without a key nothing is sent: each email is recorded as Skipped. Officers see each email's status on the request and
approval screens.

Environment variables override user-secrets, so you can change a setting for one run without touching `.env`, for
example `Email__BrevoApiKey=invalid dotnet run --project backend/CampusSpace.Api` (a failure drill: Brevo answers 401,
the email is Failed, the approval is untouched). Never print the real key.

### Damage photos (Cloudflare R2)

Damage photos from check-in go to a **private** Cloudflare R2 bucket when all four `R2__AccountId`, `R2__AccessKeyId`,
`R2__SecretAccessKey` and `R2__Bucket` are set in `.env` (then re-run `./scripts/dev-secrets.sh`). With none of them set,
Development keeps photos in the git-ignored `backend/CampusSpace.Api/App_Data/damage-photos` folder; setting only some is
a startup error, and Production refuses to start without R2. The startup log says which store is used (`Photo store: R2`
or `Photo store: local folder`), never a key or bucket.

- The bucket is never public and no photo URL exists: `GET /api/loans/{id}/photo` (Lab Technician, Facilities Officer)
  streams the photo through the API with `Cache-Control: private, no-store`. If R2 can't be reached it answers 503
  (502 if R2 answers with an error), and a check-in with a photo is refused the same way with nothing saved.
- Object keys are random (`<32 hex>.jpg|png`) with no names or ids; the database stores the key, content type and size.
- With R2 configured, startup copies any photos left in the local folder into the bucket once (it logs the count and
  leaves the folder; delete it yourself once the count looks right).
- A rare orphan (an uploaded photo whose check-in failed and whose clean-up delete also failed) is logged as
  `Orphaned damage photo object <key>`. It is private and unreferenced. To clean up, compare the bucket's objects in the
  Cloudflare dashboard with the keys in use (read-only):
  `select "DamagePhotoKey" from "EquipmentLoans" where "DamagePhotoKey" is not null;`

## Run the agent service

The API's `/health` includes an `agent-service` check. When the agent service is down, the overall status is
`Degraded`, but the endpoint still returns HTTP 200.

1. Install [uv](https://docs.astral.sh/uv/). It installs Python 3.11 by itself.
2. In `.env`, set `AgentService__ServiceKey` to at least 32 characters (`openssl rand -hex 32`). The service refuses to start otherwise.
   Re-run `./scripts/dev-secrets.sh` so the API has the same key.
3. Create the agent service's own checkpoint database. Paused approvals are stored there, so they survive a restart.
   Set `AGENT_DB_PASSWORD` in `.env` (hex, at least 32 characters: `openssl rand -hex 16` or `-hex 32`), start the
   database, and run the script. It is safe to re-run. It creates the role `campusspace_agent`, which can't reach any
   business table, plus its database, and fills in `AGENT_CHECKPOINT_URL` in `.env` if that is empty. It never prints a
   secret.
   ```bash
   ./scripts/dev-agent-db.sh
   ```
   Without `AGENT_CHECKPOINT_URL`, the service starts only with `AGENT_ENV=development`, which keeps checkpoints in a
   SQLite file instead.
4. Run it and check it (`/health` shows `"checkpointer": "postgres"` and `"checkpointer_ok": true`):
   ```bash
   cd agent-service
   uv sync
   uv run uvicorn app.main:app --reload --port 8000
   curl -s localhost:8000/health
   ```
5. Lint and test. The tests set their own fake keys and never read `.env`. The Postgres checkpointer tests run only
   when `AGENT_TEST_POSTGRES_URL` points at a throwaway database (CI provides one; see agent-service/README.md):
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

### Status notifications (UC08)

While a student or lecturer is signed in, the app polls their requests and shows an Android notification when
Facilities approves, rejects, re-plans or cancels one, when it is closed automatically, or when the agent could not
prepare a proposal. Tapping it opens the request. The app asks for the notification permission (Android 13+) once,
after the first successful submit; if you deny it, everything else works as before.

**Limitation:** notifications are local, not push (no FCM). They appear only while the app process is alive: in the
foreground, or shortly after it goes to the background until Android freezes or stops it. Nothing is shown for changes
made while the app was closed, and nothing is notified at sign-in (the first poll only records the current statuses).
A tap after the app was fully closed just opens the app.

To try it on the emulator: sign in as a requester, submit a request (allow notifications when asked), approve it as the
Facilities Officer in the web app, and wait up to 15 s (the PendingApproval poll interval). To see the prompt again,
uninstall the app (the "asked once" flag lives in its secure storage).

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
The deployed (Production) API seeds the same accounts with a different password from the `Seed__DemoPassword`
environment variable; it is given in the submitted report, never in this public repository.

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

## Live URLs

Fill these in after the first deploy ([runbook](docs/deploy/RUNBOOK.md)). Open them in an incognito window before
submitting; free Render services sleep, so the first call takes about a minute (warm-up: runbook step 7).

| What | URL |
|------|-----|
| Web app (Vercel) | `https://<project>.vercel.app` |
| API health (database + agent service) | `https://campusspace-api.onrender.com/health` |
| API Swagger | `https://campusspace-api.onrender.com/swagger` |
| Agent service health | `https://campusspace-agent.onrender.com/health` |

The demo accounts are the ones under "Test accounts"; their deployed password is in the submitted report, never here.

## Deploy

Hosting: **Neon** (PostgreSQL 16, Singapore) + **Render** (the API and the agent service from their Dockerfiles, free,
Singapore, `render.yaml` Blueprint) + **Vercel** (React, `web/vercel.json`). Step by step, with every secret generated
on your own machine or by Render and pasted only into a dashboard: **[docs/deploy/RUNBOOK.md](docs/deploy/RUNBOOK.md)**.

- **Who talks to whom.** Browser and APK → API over HTTPS with a JWT. API → agent service (`X-Service-Key`) and agent
  service → API's `/internal/agent-tools` (`X-Agent-Key`), both over the services' public HTTPS URLs (free Render
  services can't receive private-network traffic). API → Neon **direct** endpoint as `campusspace_app` (DML only);
  agent service → Neon **pooled** endpoint as `campusspace_agent` (its checkpoint database only); migrations and roles
  from your Mac as the Neon owner (`scripts/neon-db.sh`, roles in `docs/deploy/neon-roles.sql`).
- **Two health routes on each service.** `/health/live` runs no check (no database, no outbound call): it is Render's
  frequent health check, so a ping never wakes Neon or the other service. `/health` checks the dependencies (API:
  database + agent service; agent: checkpointer) and is the evidence URL.
- **Images.** `backend/Dockerfile` (ASP.NET 8 chiseled runtime, non-root, ~70 MB) and `agent-service/Dockerfile`
  (Python 3.11 slim + `uv sync --locked`, non-root, ~89 MB). Build locally: `docker build -t campusspace-api backend`,
  `docker build -t campusspace-agent agent-service`.
- **Check a deployment:** `./scripts/verify-deploy.sh --api <API URL> --agent <agent URL> --web <Vercel URL>`.

### Environment variables

Set these on the host, never in Git. `appsettings.Production.json` holds no secrets. The API's names are in environment
variable form (`__` stands for the `:` in its configuration keys).

| Component | Variable | Secret | Required in Production | Notes |
|-----------|----------|--------|------------------------|-------|
| API | `ASPNETCORE_ENVIRONMENT` | no | yes | `Production` |
| API | `PORT` | no | set by Render | the API listens on `http://0.0.0.0:$PORT`; TLS ends at Render's proxy |
| API | `ConnectionStrings__Default` | **yes** | yes | the restricted app role on Neon's DIRECT endpoint, `SslMode=VerifyFull` (startup refuses below `Require`) |
| API | `Jwt__Key` | **yes** | yes | ≥ 32 bytes; generated by Render on the API only (`render.yaml`) |
| API | `Jwt__Issuer`, `Jwt__Audience` | no | yes | `campusspace-api`, `campusspace-clients` |
| API | `AgentService__BaseUrl` | no | yes | the agent service's URL |
| API | `AgentService__ServiceKey` | **yes** | yes | X-Service-Key, ≥ 32 chars, differs from `AgentTools__Key`; generated by Render (env group) |
| API | `AgentTools__Key` | **yes** | yes | X-Agent-Key, ≥ 32 bytes; generated by Render (env group) |
| API | `Cors__AllowedOrigins__0` | no | yes | the Vercel URL, e.g. `https://<app>.vercel.app` (https, no path) |
| API | `Cors__AllowedOrigins__1` | no | optional | `http://localhost:5173` (local React against the deployed API) |
| API | `R2__AccountId`, `R2__AccessKeyId`, `R2__SecretAccessKey`, `R2__Bucket` | **yes** (the key pair) | yes | the production bucket (private) |
| API | `Seed__DemoPassword` | **yes** | yes | demo accounts' password, ≥ 12 characters, not the Development one (`openssl rand -hex 8`: 16 characters, typeable on a phone) |
| API | `Email__BrevoApiKey` | **yes** | optional | without it every email is Skipped |
| API | `Email__FromAddress` | no | with a Brevo key | a sender verified in Brevo |
| API | `Email__RedirectAllTo` | no | **yes** with a Brevo key | the seeded addresses are fake (`@campusspace.local`); every email goes here (startup warns otherwise) |
| Agent service | `AgentService__ServiceKey`, `AgentTools__Key` | **yes** | yes | the same values as the API's (the shared Render env group) |
| Agent service | `API_BASE_URL` | no | yes | the API's URL (for `/internal/agent-tools`) |
| Agent service | `GOOGLE_API_KEY` | **yes** | with LLM agents | Gemini key |
| Agent service | `AGENT_LLM_AGENTS` | no | optional | e.g. `supervisor,venue_matching,equipment_allocation,policy_cost` |
| Agent service | `PLANNER_MODEL`, `WORKER_MODEL`, `PLANNER_THINKING`, `WORKER_THINKING` | no | optional | model ids and thinking levels |
| Agent service | `AGENT_CHECKPOINT_URL` | **yes** | **yes** (startup refuses without it) | `postgresql://` URL of the agent's own checkpoint role and database/schema, which can't reach any business table; never logged or shown on `/health` |
| Agent service | `AGENT_CHECKPOINT_PATH` | no | **never** | SqliteSaver file, the development fallback (`AGENT_ENV=development` without a URL) |
| Agent service | `AGENT_ENV`, `AGENT_FAULT*` | no | **never** | development only: the SQLite fallback and the failure drills |
| Local only | `AGENT_DB_PASSWORD` | **yes** | **never** | the agent role's password for `./scripts/dev-agent-db.sh` (hex, ≥ 32 chars) |
| Web (Vercel) | `VITE_API_URL` | no (public in the bundle) | yes | the API's https URL, read at **build** time; never put a secret in a `VITE_` variable |
| Mobile (APK) | `API_URL` (`--dart-define`) | no | yes | the API's https URL; a release build allows HTTPS only |

### What Production changes (`appsettings.Production.json`)

- **Migrations never run on startup** (`Database:MigrateOnStartup` false): the app role has no DDL rights. If a migration
  is pending, the API refuses to start with this instruction. Apply migrations with the database **owner** role:
  `./scripts/neon-db.sh migrate` (the idempotent `dotnet ef migrations script`, applied with psql; the owner URL is read
  with a hidden prompt; runbook step 2). The app role has DML only (`docs/deploy/neon-roles.sql`: CONNECT, DML on the
  tables, USAGE/SELECT on the sequences, default privileges for future tables).
- **The seed runs on startup with reference data only** (`Seed:OnStartup`): demo accounts, clubs, buildings, rooms,
  features, equipment types and items, pricing rules and policy. No requests, bookings or blackouts. It only selects
  and inserts (DML rights), adds missing rows and never overwrites, so it is safe on every start.
- **TLS to the database is required** (`Database:RequireSsl`): a connection string below `SslMode=Require` stops startup.
  For a local Production smoke run against Docker Postgres (no TLS), override it for that run only:
  `Database__RequireSsl=false`.
- **Swagger is on** at `/swagger` (Bearer scheme; the internal agent-tool routes stay hidden).
- **Behind Render's proxy**: `X-Forwarded-For`/`X-Forwarded-Proto` are honoured (only the proxy's last entry), so the API
  sees HTTPS and redirects plain HTTP to it, except `/health` and `/health/live` (Render's health check calls
  `/health/live` over HTTP).
- **CORS**: only the configured origins; startup refuses an empty list, `*`, a path or plain `http` other than localhost.
- **Logs**: readable console lines (time, level, trace id, source), no request bodies, no secrets.
- **The agent service gets 4 minutes to start a run** (`AgentService:StartTimeoutMinutes`, 2 elsewhere): a sleeping
  free agent service takes about a minute to wake, and the poller retries the start meanwhile.
- `/health` checks the database and the agent service. It does not check R2: every ping would spend R2 operations,
  and an R2 outage only affects damage photos (503 on those requests), which a restart would not fix.

Startup order: PostgreSQL → migrations (owner role) → agent service → API (seeds) → React → Flutter.

