# CampusSpace AI: SE3090 group project

CampusSpace AI books campus rooms and equipment. Requesters submit a booking objective from Flutter.
A LangGraph multi-agent service drafts a proposal. A Facilities Officer approves it in React, and .NET books it in one transaction.
Four students each own one business component (A–D) end-to-end, including one distinct agent.

**Source of truth:** `docs/CampusSpace_AI_Project_Plan.pdf` plus `docs/plan-addendum.md`. The addendum wins where they differ.
To look up a plan section, search the text copy `docs/plan.txt` (for example `grep -n '10.8' docs/plan.txt`).
The PDF is the official copy and wins if it and `docs/plan.txt` ever disagree.
If a request conflicts with them, **stop and ask** before doing anything.

## Repo layout (§7.2)

```
backend/CampusSpace.Api/     Controllers/ Dtos/ Services/ Data/ (AppDbContext, Configurations/, Migrations/, Seed.cs)
                             Models/ Agents/ (AgentClient, AgentRunPoller) Internal/ (X-Agent-Key tool routes)
backend/CampusSpace.Tests/   xUnit unit + integration (Testcontainers)
web/                         React + TS + Vite (src/: api/ auth/ layout/ features/<area>/ ui/ test/)
mobile/                      Flutter
agent-service/app/           main.py, graph.py, workers/, tools.py, schemas.py, validation.py (V01–V12)
agent-service/eval/          eval_dataset.json (golden cases)
agent-service/tests/         pytest
docs/                        plan, addendum, ADRs, ER diagram, report assets
.github/workflows/ci.yml
```

## Stack (pinned)

| Layer | Version |
|-------|---------|
| .NET | 8 (SDK 8.0.423), pinned via `global.json` |
| EF Core, Npgsql.EntityFrameworkCore.PostgreSQL, JwtBearer | `8.*` (newer majors target .NET 10) |
| PostgreSQL | 16 in Docker (local) |
| Node | 24 |
| Web | React 19 + TypeScript + Vite (see `docs/adr/README.md`) |
| Mobile | Flutter 3.47 stable (Android only) |
| Agent service | Python 3.11 via `uv` |

- Every `Microsoft.*` and `Npgsql` package stays on 8.x, **including transitive ones**. Before adding a third-party package,
  check its net8.0 dependencies. For example, use Serilog.AspNetCore 8.0.3, not 10.0.0, because 10.0.0 pulls in Microsoft.Extensions.* 10.x.
- After adding a package, run the `--include-transitive` check in Commands and show its (empty) output.

## Local ports (fixed)

| Service | Port |
|---------|------|
| API | **5080** (never 5000: macOS AirPlay uses it) |
| Agent service | 8000 |
| React (Vite) | 5173 |
| PostgreSQL | 5432 |

## Architecture rules (§7.1)

1. Clients (React, Flutter) call only ASP.NET Core `/api/...` with a JWT.
2. Only ASP.NET Core calls the agent service, always with `X-Service-Key`.
3. The agent service has no DB credentials. Its tools call read-only `/internal/agent-tools/...` routes with a separate `X-Agent-Key`.
4. .NET persists the audit record (runs, steps, tool calls, validation results, decisions).
5. The high-impact action (booking) is executed by .NET only after officer approval, in one DB transaction.
6. Long-running work is async: start → 202 Accepted → a background poller tracks status.

Addendum rules:
- Booking policy lives in `PolicySettings`. **No policy number is hard-coded anywhere.** .NET reads policy through `IPolicySettingsService`.
  Python reads it only through `GET /internal/agent-tools/policy`.
- Equipment covered by a room feature (`EquipmentTypes.CoveredByFeatureCode`) becomes `qty 0, source "room_builtin"`. It is unpriced and not counted by V08.

## Database conventions (§8)

- Target 3NF. Surrogate IDENTITY keys. Natural identifiers (email, room code, asset tag) are UNIQUE, not the PK.
- `numeric(10,2)` for money. `timestamptz` (UTC) for every timestamp. NOT NULL by default. CHECK constraints for business rules.
- Index every FK column. `ON DELETE RESTRICT` by default. Use CASCADE only for children that are meaningless alone.
- Default PascalCase table names. Fluent API or one `IEntityTypeConfiguration<T>` per entity.
- Every business table has `CreatedAt`/`UpdatedAt`, set in an overridden `SaveChangesAsync`.
- Raw SQL (for example the exclusion constraint) goes in migrations via `migrationBuilder.Sql(...)`.
- Never store model hidden reasoning, tokens or secrets. Store only summaries, inputs, outputs and timings.

## API conventions (§9)

- Public routes live under `/api`. Internal agent-tool routes are hidden from public Swagger.
- Status codes: 201 + `Location` on create, 204 on delete, 400 validation Problem Details, 401 missing or expired token,
  403 wrong role or not owner, 404, 409 conflict (`23505` and `23P01` map to 409).
- List endpoints take `?search=&sort=&page=&pageSize=` and return `{ items, page, pageSize, total }`.
- Errors: global exception middleware. Full details are logged with the traceId. The client gets RFC 9457 Problem Details.
- `[Authorize(Roles = "...")]` on every non-public action (default deny). Ownership checks happen in services.

## Backend layering

`Controller → IService/Service (AddScoped) → AppDbContext`. There is no separate repository layer.
DTOs are `record`s with data annotations (`[Required]`, `[Range]`, `[EmailAddress]`). Policy-driven checks belong in services,
because annotations cannot read `PolicySettings`.

## Agent service conventions

- Anchor file paths to the source file (`Path(__file__).resolve().parents[N] / ...`), never to the working directory.
  Never write `"../.env"`. This applies to `.env`, eval datasets and prompt files.
- Tests that load files use `monkeypatch.chdir` plus a temporary file, and never read the real `.env`.

## Secrets

- Never commit secrets to Git.
- .NET: `dotnet user-secrets`.
- Everything else: `.env` files, which are git-ignored.
- Every variable name (without its value) goes in `.env.example`.

## Team ownership (§18)

| Member | Component | Agent | React | Flutter | Also leads |
|--------|-----------|-------|-------|---------|------------|
| 1 | A Facilities | Venue Matching | Rooms, room form, blackouts | Browse rooms, room schedule | Seed data, exclusion constraint, availability query |
| 2 | B Equipment | Equipment Allocation | Equipment types/items, loans | Technician handover + check-in with camera | Email integration, k6 performance tests |
| 3 | C Requests | Supervisor + graph skeleton | Requests table/detail, clubs | New request stepper, my requests | Auth (shared), Flutter shell, agent-service scaffolding |
| 4 | D Approval | Policy and Cost | Dashboard, approval screens, reports, agent monitor, booking policy | Quotation view, notifications | CI, deployment, React shell, eval harness |

## How to work (rules for Claude)

- Do one task at a time. Propose a plan before any multi-file change.
- Do not edit another component's code unless asked.
- Add or update tests with every behaviour change.
- Run the build and tests before saying "done".
- Never add a package or upgrade a major version without saying why.
- Keep commits small and use Conventional Commits (`feat(b): ...`, `fix(api): ...`).
- Project rules go in CLAUDE.md, never only in personal memory.
- End every task with:
  1. Files changed
  2. How you verified
  3. Three things I must understand for the viva
  4. A 2-line entry for my AI usage log

## Git workflow

- Never commit to `main`. Start every task from an up-to-date `main` on a new branch named `<type>/<component>-<short-desc>`
  (for example `chore/repo-skeleton`, `feat/a-rooms-crud`, `feat/shared-auth`).
- Keep commits small, using Conventional Commits.
- When a task is done and verified, push and open a PR with `gh pr create`. The PR body lists what changed,
  how it was verified, and which plan section it implements.
- Never merge PRs yourself. The user merges.

## Commands

Database (credentials come from `.env`; psql reads them from the container's env):

```bash
docker compose up -d                 # start PostgreSQL 16
docker compose ps                    # wait for (healthy)
docker compose exec db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select version();"'
docker compose down                  # stop, keep data
docker compose down -v               # stop and wipe the pgdata volume
```

Backend (needs dotnet-ef 8.* installed globally: `dotnet tool install -g dotnet-ef --version "8.*"`):

```bash
./scripts/dev-secrets.sh                            # .env -> dotnet user-secrets (never prints values, re-runnable)
dotnet build backend
dotnet test backend                                 # Docker must be running (Testcontainers postgres:16)
dotnet run --project backend/CampusSpace.Api        # http://localhost:5080 (/health, /swagger); Development auto-migrates
dotnet ef migrations add <Name> --project backend/CampusSpace.Api -o Data/Migrations
dotnet ef database update --project backend/CampusSpace.Api
dotnet list backend package --include-transitive | grep -E " 9\.| 10\."   # must print nothing Microsoft.*/Npgsql
```

Agent service (run from `agent-service/`; reads the repo-root `.env`, and `AgentService__ServiceKey` must be ≥ 32 chars):

```bash
uv sync                                             # create .venv from uv.lock (Python 3.11)
uv run uvicorn app.main:app --reload --port 8000    # http://localhost:8000 (/health, /docs)
uv run ruff check .
uv run pytest -q                                    # never reads the real .env
curl -s localhost:8000/health                       # the API's /health shows it as check "agent-service"
```

Web (run from `web/`; Node 24 per `.nvmrc`; `VITE_API_URL` comes from the repo-root `.env`):

```bash
npm ci
npm run dev                                         # http://localhost:5173 (strictPort); needs the API on :5080
npm run lint                                        # oxlint
npm test -- --run                                   # Vitest + Testing Library + MSW; no real network, no .env
npm run build                                       # tsc -b && vite build
```

Web conventions: call the API only through `api` in `src/api/client.ts`. Server data goes through TanStack Query and
the session through `useAuthStore` (ADR-1). Guard routes with `<ProtectedRoute roles={...}>` using `Roles`/`STAFF_ROLES`
from `src/auth/roles.ts`, and add the matching entry in `src/layout/navItems.tsx`. Show server errors with
`parseProblem`/`applyFieldErrors`. Copy `features/users/` for new list pages. Never prefix a secret with `VITE_`.
Tests use `renderApp(route, { role })` from `src/test/utils.tsx` and MSW handlers (`server.use(...)`).

Mobile (run from `mobile/`; Flutter 3.47.3 stable, Android only; the API URL is a build-time `--dart-define`):

```bash
flutter pub get
flutter analyze                                     # must report no issues
flutter test                                        # fakes + provider overrides; no network, no emulator
flutter emulators --launch Pixel_10                 # then `flutter devices` for the emulator id
flutter run -d <emulator-id> --dart-define=API_URL=http://10.0.2.2:5080   # needs the API on :5080
```

Mobile conventions: call the API only through `dioProvider` (`lib/core/api/dio_client.dart`) inside a feature
repository. One plain `AsyncNotifier` per feature (ADR-2); no code generation (no freezed, riverpod_generator or
build_runner) and hand-written `fromJson`. The session lives in `authControllerProvider` and only in
flutter_secure_storage, never SharedPreferences. Add routes in `lib/core/router.dart`; the redirect guard handles
auth. Show server errors with `Problem.from(e)` and `problem.fieldError('field')`. Validators mirror the DTO
annotations in `lib/core/validators.dart`. Cleartext HTTP is allowed only in debug builds and only to
`10.0.2.2`/`localhost`. Tests override `tokenStorageProvider`/`authRepositoryProvider` with fakes (`test/helpers.dart`).

Auth smoke test (API running; demo accounts are seeded in Development, password in README "Test accounts"):

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"admin@campusspace.local","password":"CampusSpace#2026"}' | jq -r .accessToken)
curl -s http://localhost:5080/api/auth/me -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:5080/api/users?page=1&pageSize=20" -H "Authorization: Bearer $TOKEN"
```

Auth conventions: the fallback policy denies anonymous access, so only mark `[AllowAnonymous]` when you mean it. Use
`[Authorize(Roles = Roles.X)]` (never string literals), `User.GetUserId()` for the caller's id, `ConflictException`
for 409s from services, and `PageQuery`/`PagedResult<T>`/`ToPagedResultAsync` for list endpoints.
Tests get tokens from `TestAuth.CreateClient(factory, Roles.X)`. For writes, use
`TestAuth.CreateUserClientAsync(factory, Roles.X)`: it inserts a real user, because audit rows store the caller's id as an FK.

Shared foundation conventions: services get the caller from `ICurrentUser` (null outside a request). Mark a business
entity `IAuditable` and `AppDbContext.SaveChangesAsync` audits its inserts, updates and deletes (property names only,
never values or `PasswordHash`). `ExecuteUpdate`/`ExecuteDelete` are not audited. Log non-entity events with
`IAuditService` (constants in `AuditActions`), never with passwords or tokens. Throw `BusinessRuleException(field, message)`
for a rule-based 400 with a field error. A DB rule that needs a specific 409 is mapped by `ConstraintName` in
`GlobalExceptionHandler.Map`. Prefer EF-generated constraints and indexes over raw SQL.

If Docker Hub is unreachable, Testcontainers cannot pull its Ryuk reaper image. Run the tests with
`TESTCONTAINERS_RYUK_DISABLED=true` (local only; never commit it).

CI (`.github/workflows/ci.yml`, job `backend` runs restore, Release build with warnings as errors, and tests):

```bash
gh pr checks --watch                                # watch the current branch's PR run until it finishes
gh run list --limit 1                               # latest run and its URL
gh run view --log-failed                            # read the failing step's log
```

<!-- Add web/mobile/agent-service commands as each app is scaffolded. -->
