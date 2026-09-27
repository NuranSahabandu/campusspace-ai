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
`parseProblem`/`applyFieldErrors`. Build list pages from `useServerTable` + `usePagedQuery` + `<ServerDataGrid>`
(copy `features/users/` or `features/clubs/`). Do writes through `useApiMutation` (toast, invalidate, 400 field errors,
409 `conflictField`, traceId toast). Confirm destructive actions with `ConfirmDialog`. Show timestamps with
`formatDateTime` (Asia/Colombo), and turn date inputs into filters with `campusDayBounds`. Never prefix a secret with `VITE_`.
Turn a native `datetime-local` value into an API instant with `campusLocalToIso` (campus time, `+05:30`). For a 409 that
belongs to no form field (for example deleting a row that is still in use), pass `conflictMessage` to `useApiMutation`.
Tests use `renderApp(route, { role })` from `src/test/utils.tsx` and MSW handlers (`server.use(...)`).
Equipment type pickers use `useEquipmentTypeOptions` (one request with pageSize=100, the PageQuery max; never loop over
pages). Read a filter that another page links to from the URL with `useSearchParams`, ignoring invalid values (see
`?typeId=` on /equipment/items). Show money with `formatLkr` (`src/ui/formatLkr.ts`).
Show an API `DateOnly` ("yyyy-MM-dd") with `formatDateOnly` (never `new Date()`, which shifts it west of UTC), and use
`campusToday()` for a date input's `min` and past-date checks. Policy forms name their fields by the snake_case setting keys,
because `parseProblem` only lower-cases the first letter (`max_duration_hours` stays as is). Put extra content in a
`ConfirmDialog` (for example a list of changes) as its `children`.
Request statuses, labels, chip colours and the All/Open/Approved/Closed groups live only in
`features/requests/requestStatus.ts` (mirrors the mobile `request_status.dart`); show them with `RequestStatusChip`. Show a
start/end pair with `formatCampusTimeRange`. The `api` client sends arrays as repeated params (`?status=A&status=B`).
A list whose filters must survive opening a row uses `useServerTable({ urlState: true })` and keeps its own filters in
the URL through `table.updateUrl` (see `features/requests/BookingRequestsPage.tsx`). Render untrusted user text (request
notes) as a plain React text child with `whiteSpace: 'pre-wrap'`, never as HTML or markdown.

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
Parse list responses with `PagedResult<T>.fromJson(json, T.fromJson)` (`lib/core/api/paged_result.dart`). Build query
parameters in a filter value class whose `toQuery()` drops empty values (see `RoomFilter`: `minCapacity` 0 would be a 400).
Riverpod 3 retries failing providers automatically, so pass `retry: (_, _) => null` to providers whose screen has a Retry
button. Guard role-specific routes in `authRedirect` (for example `/rooms` is for Student and Lecturer only). Screen tests
override `facilitiesRepositoryProvider` and use `pumpRoomsScreens`; router tests use `pumpApp(..., overrides: [...])`.
Campus time on mobile: build API times with `campusIso(date, time)` (always `+05:30`) and show API instants with the
`formatCampus*` helpers in `lib/core/campus_time.dart`; never use `toLocal()`, `DateTime.now()` or `TimeOfDay.now()`
for campus dates and times (read `clockProvider`, which tests override). Show money with `formatLkr` (`lib/core/format.dart`). Status labels, colours and
the My requests filter groups live only in `lib/features/requests/request_status.dart`. Date and time pickers read the
live policy (`policyProvider`) through the pure rules in `time_rules.dart`. Requests screen tests use
`pumpRequestsScreens` + `stubRequestsReferenceData`; fixtures in `test/fixtures/requests.dart` are real API responses.
A request's history rows carry `changedById`; show "You" by comparing it with the requester's id, never by name.

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

Facilities conventions (Component A): `RoomTypes` is the only list of room types (CHECK + `[ValidRoomType]`). A deleted
row that is still referenced raises 23503, which maps to 409 "In use"; services check FKs before inserting and return a 400
on the field instead. Time ranges (`RoomBlackouts.TimeRange`, and later bookings) are UTC `tstzrange` `[start, end)`, enforced by a
CHECK; build them with `new NpgsqlRange<DateTime>(start.UtcDateTime, true, end.UtcDateTime, false)`. Rooms have no opening
hours: `PolicySettings.opening_hours` (Component D) is the only source. Endpoint tests create their own building and rooms
with `FacilitiesTestData` (the shared test database is not seeded). Feature and EquipmentType codes are immutable after
creation, because other tables and the agent service reference them by code. A changed Feature code is a 409 "In use" when it
is referenced, otherwise a 400 on Code. A changed EquipmentType code is always a 400 on Code.

Pricing and policy conventions (Component D): Booking limits (lead time, advance window, duration, capacity ratio,
granularity, opening hours, cancellation, open-request cap) are read only through IPolicySettingsService. Never hard-code
48 h, 60/90 days, 8 h, 3×, 30 min. `GetAsync()` returns a typed `PolicySnapshot` read from the DB on every call; any
signed-in client reads the values from `GET /api/policy-settings/public` (the officer route has the metadata). Pricing
rules in effect are read-only. Change a price by adding a rule with a later ValidFrom. Price a booking with
`IPricingRuleService.GetEffectiveRuleAsync` (latest ValidFrom on or before the booking's campus date). Audit logs store
property names only, except PolicySettings, which log old/new values per changed key (addendum A.1; they aren't personal
data or secrets). Campus dates come from `CampusTime` (`Today(TimeProvider)`, `DateOf(instant)`), never from the UTC date.
New policy keys need a migration (the CHECK lists them) plus a `PolicySettingDefaults` entry. Tests that change policy or
need exact pricing statuses use `fixture.CreateIsolatedFactoryAsync()` (their own database); pricing tests on the shared
database use `PricingTestData.UniqueFutureDate()`.

Booking request conventions (Component C): Request status changes only through IRequestStateMachine (never assign
Status directly); every change writes a RequestStatusHistory row in the same SaveChanges. `RequestStatuses` is the only
status list, and `RequestStatuses.Open` is what counts toward `max_open_requests`. Object-level checks (a requester
reading someone else's request) throw `ForbiddenException` (403). Submit takes a per-requester
`pg_advisory_xact_lock` in its own transaction, so the open-request count and the insert are atomic. Request times are
accepted with any offset and returned as UTC. `Notes` is untrusted text: never interpret, log or echo it in messages.
`RequiredFeatures` is a `text[]` with no FK, so FeatureService checks it before a feature's delete or code change.
Endpoint tests use `BookingRequestTestData` (`StudentRepAsync`, `Body`, `MoveAsync` through the real state machine).
`FutureStart(n)` is 10:00 on the n-th weekday ahead (3–40, so outside the lead time and inside the student window).
Cancellation (`POST /api/booking-requests/{id}/cancel`): the owner (reason optional) or a Facilities Officer (reason
required). Free until `free_cancellation_hours` before the start; an owner's later cancellation of an Approved request
sets `IsLateCancellation`. Late cancellations are flagged, not charged. Officer cancellations set `CancelledByOfficer`
and are never late, and pre-approval cancellations are never late. Cancellable statuses come only from the state
machine table. Cancel locks the request row (`SELECT … FOR UPDATE`) first, and Phase 3 approve/reject must take the same
lock. An Approved cancel sets the booking to Cancelled (releases room and equipment), voids the live quote
(`QuotationService.VoidLiveAsync`, static because QuotationService already depends on IBookingRequestService), and moves
the request, all in one transaction. Tests insert an owned Approved booking with `BookingTestData.InsertApprovedBookingAsync`.

Bookings and availability: Booking-time rules (V05/V06) live only in IBookingWindowRules; submit, availability and the
approval re-check call it (`CheckSlot` = V05, `CheckTiming` = V06; availability uses only `CheckSlot`). Its messages match
mobile `time_rules.dart`, so keep them in step. Double booking is prevented by the Bookings exclusion constraint
(`no_room_overlap`, active statuses only); code checks are for friendly errors, the constraint is the guarantee.
`BookingStatuses.Active` is the only list of statuses that hold a room. Build every `tstzrange` with
`CampusTime.UtcRange(start, end)` and compute overlaps in SQL with `TimeRange.Overlaps(range)` (`&&`), never in memory.
The room schedule labels bookings only "Booked" (never the requester or purpose). No endpoint creates bookings until
the Phase 3 approval; tests insert them with `BookingTestData.InsertBookingAsync`. Tests about "now" (lead time,
advance window) use `fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(...))` instead of changing the policy.

Equipment reservations and blackout clashes: Equipment is held by EquipmentReservations of Active bookings; availability =
serviceable (Available + OnLoan) - reserved in overlapping windows, computed in SQL (`IEquipmentAvailabilityService`).
A reservation's TimeRange is always a copy of its booking's TimeRange, kept only for the GiST (TypeId, TimeRange) index.
Over-allocation is prevented by ReserveAsync: per-type advisory locks in ascending TypeId order inside the approval
transaction. It never calls SaveChanges (the caller commits) and skips qty-0 (`room_builtin`) lines. All advisory locks
use AdvisoryLocks namespaces (two-int key form, `AdvisoryLocks.LockAsync`). The approval transaction runs at READ
COMMITTED: the exclusion constraint guards rooms and advisory locks guard equipment. Do not use Serializable (plan App.
A.3 is superseded here); ReserveAsync refuses any other isolation level. A blackout never cancels bookings automatically;
it reports clashes for the officer to handle (`clashes` on the create response, `GET .../blackouts/{id}/clashes`).
Tests insert reservations with `EquipmentTestData.InsertReservationAsync` (copies the booking's range).

Quotation conventions (Component D): Prices are computed only by IQuotationCalculator. The agent's quote is checked
against it (V09). Lecturer bookings are fully exempt (room and equipment); students pay room rules + equipment fees.
Exemption is decided by the effective room PricingRule's IsExempt for the requester's role and the booking's campus date,
and shown as a whole-quote discount: every line is priced normally, Discount = Subtotal, DiscountReason
"Lecturer exemption (academic use)", `IsExempt` true, Total = 0. Never price a line at 0 because of the exemption.
Room Qty is the duration in hours rounded to 2 places first (`QuotationCalculator.Hours`), and every
LineTotal = round(Qty × UnitPrice, 2, AwayFromZero) (`QuotationCalculator.Line`), which `CK_QuotationLines_LineTotal`
enforces. Equipment is priced per booking (FeePerBooking), qty-0 lines are skipped, repeated types summed. UnitPrice is a
snapshot. At most one Draft or Issued quote per request (`IX_Quotations_RequestId_Live`). `CreateDraftAsync` needs the
caller's transaction, voids the old Draft with ExecuteUpdate plus a hand-written audit row, and never calls SaveChanges.
Quote reads use the request read rule (`IBookingRequestService.EnsureCanReadAsync`). Calculator tests seed their own
database with `Seed.SeedAsync`; persistence and endpoint tests use `QuotationTestData`.

If Docker Hub is unreachable, Testcontainers cannot pull its Ryuk reaper image. Run the tests with
`TESTCONTAINERS_RYUK_DISABLED=true` (local only; never commit it).

CI (`.github/workflows/ci.yml`, job `backend` runs restore, Release build with warnings as errors, and tests):

```bash
gh pr checks --watch                                # watch the current branch's PR run until it finishes
gh run list --limit 1                               # latest run and its URL
gh run view --log-failed                            # read the failing step's log
```

<!-- Add web/mobile/agent-service commands as each app is scaffolded. -->
