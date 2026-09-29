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
- Status codes: 201 + `Location` on create, 202 + `Location` when the work goes on in the background (submit,
  retry-agent), 204 on delete, 400 validation Problem Details, 401 missing or expired token,
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

Agent service (run from `agent-service/`; reads the repo-root `.env`; `AgentService__ServiceKey` and `AgentTools__Key` must be ≥ 32 chars and differ):

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
`features/requests/requestStatus.ts` (mirrors the mobile `request_status.dart`); show them with `RequestStatusChip`. Queries
that show requests re-fetch while one is in `REFRESHING_STATUSES` (AgentProcessing, RevisionRequested; 3 s): pass
`refetchInterval: refreshIntervalFor(statuses)` (`usePagedQuery` takes it too), never a hand-written interval. Show a
start/end pair with `formatCampusTimeRange`. The `api` client sends arrays as repeated params (`?status=A&status=B`).
A list whose filters must survive opening a row uses `useServerTable({ urlState: true })` and keeps its own filters in
the URL through `table.updateUrl` (see `features/requests/BookingRequestsPage.tsx`). Render untrusted user text (request
notes) as a plain React text child with `whiteSpace: 'pre-wrap'`, never as HTML or markdown.
Cancel a request only through `CancelRequestDialog` (`features/requests/`): an officer reason is required (≤ 500), a
409 title is shown exactly as sent, and it invalidates the request and room queries. Which statuses offer it comes only
from `isCancellable`/`CANCELLABLE_STATUSES` in `requestStatus.ts` (mirrors `RequestStateMachine` and mobile
`cancellable`). Show "Late"/"By office" with `CancellationFlags`. Blackout clashes (both the add-blackout warning and
a row's clash list) render `ClashList` from `useBlackoutClashes` (under `roomsKeys.all`, so a cancel refreshes them). The blackout
list's `clashCount` is counted in SQL with the same predicate as `ClashesOf`; keep the two in step. Show an image that
needs auth (for example `GET /api/loans/{id}/photo`) by fetching it through `api` with `responseType: 'blob'` and
`useBlobImageRef`, which revokes the object URL on unmount; never point a bare `<img src>` at the API.

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
the My requests filter groups live only in `lib/features/requests/request_status.dart`. Providers that show requests
re-fetch at `RequestStatuses.refreshIntervalFor(status)` (AgentProcessing and RevisionRequested 3 s, PendingApproval
15 s, others never; a list uses `shortestRefreshInterval`), through `_refreshWhile`
in `requests_providers.dart` (a one-shot timer cancelled on rebuild/dispose). Date and time pickers read the
live policy (`policyProvider`) through the pure rules in `time_rules.dart`. Requests screen tests use
`pumpRequestsScreens` + `stubRequestsReferenceData`; fixtures in `test/fixtures/requests.dart` are real API responses.
A request's history rows carry `changedById`; show "You" by comparing it with the requester's id, never by name.
Room schedule: `roomScheduleProvider((roomId:, date:))` with the day in `scheduleDateProvider(roomId)`; send campus
dates to the API with `campusDateParam` ("yyyy-MM-dd"). Riverpod 3 pauses providers that only hidden routes watch, so
an invalidated list reloads when its screen shows again. Cancel: the button's statuses come only from
`RequestStatuses.cancellable`, and it shows only when `currentUserIdProvider` is the requester. The late warning uses
`isLateCancellation` (`time_rules.dart`, same rule as the server). The server's `isLateCancellation` is what gets shown,
and a 409 title is shown exactly as sent. Tests re-pump with `pumpWidget(SizedBox())` first to get a fresh ProviderScope.
Technician loans (mobile, UC09–UC12): `/handovers`, `/overdue` and `/loans` are LabTechnician-only in `authRedirect`, and
the technician's home body is `TodayHandoversView`. A booking's loans come from `GET /api/loans?bookingId=`; the picker
reads `GET /api/equipment-items?typeId=&status=Available`. Every checkout/check-in error title is shown exactly as sent,
and `invalidateLoanLists` reloads what changed. The check-in rules (Damaged needs a note and a photo; photo JPEG/PNG by
magic bytes, ≤ 5 MB; note ≤ 1000) are mirrored in `CheckInRules` (`features/loans/models.dart`), with the server's
messages, so keep them in step with `LoanService`/`DamagePhotoStore`. Photos come only through `photoPickerProvider`
(image_picker with maxWidth 1600, imageQuality 80) as bytes, and are uploaded as dio `FormData`. A loan that is already
checked in opens read-only. Tests use `pumpLoansScreens`, `MockLoansRepository`, `FakePhotoPicker` and the
`live*` models from `test/fixtures/loans.dart`.

Auth smoke test (API running; demo accounts are seeded in Development, password in README "Test accounts"):

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"admin@campusspace.local","password":"CampusSpace#2026"}' | jq -r .accessToken)
curl -s http://localhost:5080/api/auth/me -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:5080/api/users?page=1&pageSize=20" -H "Authorization: Bearer $TOKEN"
```

Web route guards: `/forbidden` sits outside the staff-only `ProtectedRoute` (any signed-in role reaches it, so a
requester's or technician's stored session lands there once instead of looping); every staff page goes inside it.
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
`pg_advisory_xact_lock` in its own transaction, so the open-request count and the insert are atomic; the same
transaction creates AgentRun #1 and moves the request to AgentProcessing, and submit answers 202 (see Agent integration). Request times are
accepted with any offset and returned as UTC. `Notes` is untrusted text: never interpret, log or echo it in messages.
`RequiredFeatures` is a `text[]` with no FK, so FeatureService checks it before a feature's delete or code change.
Endpoint tests use `BookingRequestTestData` (`StudentRepAsync`, `Body`, `MoveAsync` through the real state machine).
`FutureStart(n)` is 10:00 on the n-th weekday ahead (3–40, so outside the lead time and inside the student window).
Cancellation (`POST /api/booking-requests/{id}/cancel`): the owner (reason optional) or a Facilities Officer (reason
required). Free until `free_cancellation_hours` before the start; an owner's later cancellation of an Approved request
sets `IsLateCancellation`. Late cancellations are flagged, not charged. Officer cancellations set `CancelledByOfficer`
and are never late, and pre-approval cancellations are never late. Cancellable statuses come only from the state
machine table. Cancel locks the request row (`SELECT … FOR UPDATE`) first, as approve, reject and request-revision
do. An Approved cancel sets the booking to Cancelled (releases room and equipment), voids the live quote
(`QuotationService.VoidLiveAsync`, static because QuotationService already depends on IBookingRequestService), and moves
the request, all in one transaction. Tests insert an owned Approved booking with `BookingTestData.InsertApprovedBookingAsync`.

Bookings and availability: Booking-time rules (V05/V06) live only in IBookingWindowRules; submit, availability and the
approval re-check call it (`CheckSlot` = V05, `CheckTiming` = V06; availability uses only `CheckSlot`). Its messages match
mobile `time_rules.dart`, so keep them in step. Double booking is prevented by the Bookings exclusion constraint
(`no_room_overlap`, active statuses only); code checks are for friendly errors, the constraint is the guarantee.
`BookingStatuses.Active` is the only list of statuses that hold a room. Build every `tstzrange` with
`CampusTime.UtcRange(start, end)` and compute overlaps in SQL with `TimeRange.Overlaps(range)` (`&&`), never in memory.
The room schedule labels bookings only "Booked" (never the requester or purpose). Only the approval (IApprovalFinalizer)
creates bookings; other tests insert them with `BookingTestData.InsertBookingAsync`. Tests about "now" (lead time,
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

Loans: checkout only for Active bookings inside [start − checkout window, end), for a reserved type, below the
reserved quantity, with the booking row locked; the partial unique index guarantees one open loan per item.
Check-in Damaged needs a note and a photo and sends the item to UnderRepair. Late returns are flagged.
The checkout window is the policy key `checkout_window_minutes` (addendum Open question 5, decided for this value only:
a PolicySettings key that the officer edits and that is audited like the others; the agent runtime limits stay code limits).
Active includes CheckedIn, not only the plan's Confirmed. Lock order is always the booking row, then the item row
(`FOR UPDATE`, READ COMMITTED). Cancel checks for open loans under the same booking lock ("Equipment is still on loan;
check it in first"). Only checkout and check-in change OnLoan (`ILoanService`). Tests build loans with `LoanTestData`:
each booking gets its own room, type and items, never seeded rows.
Damage photos: magic-byte checked, ≤ 5 MB, random names, stored outside the web root, served only through
GET /api/loans/{id}/photo. Use `IDamagePhotoStore` only. The folder is `Storage:DamagePhotosPath` (git-ignored
`App_Data/damage-photos` by default), and the DB stores only the file name (`CK_EquipmentLoans_DamagePhotoPath`). Write
the file after every check passes and delete it if the transaction fails. Tests read `factory.DamagePhotosPath`.

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

Agent tools and workflow tables (Phase 3): Agent tools live under /internal/agent-tools, need X-Agent-Key (scheme
AgentKey, policy AgentTools), are hidden from Swagger, and are read-only (POST /quote calculates only). A user JWT can't
reach them and the agent key can't reach /api (the default and fallback policies name the JWT scheme only). Keys:
X-Agent-Key is `AgentTools:Key` (env `AgentTools__Key`, ≥ 32 bytes, checked at startup); X-Service-Key (.NET → agent
service) is `AgentService:ServiceKey` (env `AgentService__ServiceKey`, ≥ 32 bytes, checked at startup). They must differ
(`dev-secrets.sh` refuses equal values). Keys are compared in constant time and never logged; each tool call logs
method, route pattern, status and ms (no query string). Tool routes are thin: the controller calls `IAgentToolService`,
the only place tools compose the business services and resolve equipment codes to ids; never re-implement a rule
there. Tool times need an explicit offset (`IsoInstant`, "Z" or "+05:30"); tools apply CheckSlot (V05) only, like the
public endpoints. request-context returns no names, emails or user ids, and `openRequestCount` excludes the request
itself (V11: other open < cap). Agent tables store summaries, inputs, outputs and timings only — never hidden
reasoning, tokens or secrets. AgentRuns.Id is the LangGraph thread_id and is generated by .NET. `AgentRunStatuses`
is the only run status list (`Active` = live, at most one per request via `IX_AgentRuns_RequestId_Live` → 409;
`Terminal` = Completed, Rejected, Failed, Cancelled); a Failed run needs a FailureReason. Steps use `AgentStepStatuses` (Succeeded, Failed). ValidationResults
are per `Attempt` (the latest checklist is the highest). Reject/Revise decisions need a comment. AgentSteps,
AgentToolCalls and ValidationResults are append-only and not IAuditable; AgentRuns and ApprovalDecisions are. The
entity for the ValidationResults table is `AgentValidationResult` (avoids DataAnnotations.ValidationResult). Tests
call tools with `AgentToolsAuth.CreateClient(factory)` (the factory's random `AgentToolsKey`) and insert workflow rows
with `AgentRunTestData`.

Agent service workflow (Phase 3, `agent-service/`): .NET calls `/workflows` with X-Service-Key
(`AgentService__ServiceKey`); the agent service calls `/internal/agent-tools` with X-Agent-Key (`AgentTools__Key`).
Both keys are required at agent-service startup (≥ 32 chars, different). `thread_id` is AgentRuns.Id, generated by
.NET (never by a client). Contract: `POST /workflows {thread_id, request_id}` → 202 (409 existing thread, 400 bad
uuid/body); `GET /workflows/{thread_id}` → `{status, revision, interrupt, plan, proposal, officer_summary, validation,
nodes, steps, policy_snapshot, error, model, usage, started_at, completed_at, duration_ms}` (404 unknown);
`POST /workflows/{thread_id}/resume {decision: approve|reject|revise|cancel, notes}` → 202 (409 unless
`awaiting_approval`; revise needs notes). Statuses: `running | awaiting_approval | completed | rejected | failed |
cancelled`; after a restart, a checkpoint that has pending nodes but is not paused reads as failed "Agent service
restarted during the run". Validation errors are 400 and a missing key is 401 first. Money in responses is a 2-dp string; parse tool JSON
with `parse_json` (Decimal), never float. The workers are deterministic stubs that call the real tools; Phase 4 swaps
one worker at a time through `build_graph(workers=...)` / `run_worker(name, task)` (task string in, validated result out); the
supervisor's planner is switchable (see LLM agents), and `enforce_plan_rules` still fixes the step order. The policy snapshot is fetched by the
supervisor at run start and again on an officer revise (`refresh_policy`, set by human_gate; `load_policy`), never on
approve/reject/cancel; a failed re-fetch ends in "policy unavailable". The view's `policy_snapshot` is the latest one,
and the validate node (plain code, V01–V12 in `app/validation.py`) reads every number from it, never a literal; V05/V06 mirror `BookingWindowRules` messages, so keep
them in step. V02/V07 (and finalize's re-check) say "Availability check failed: <the tool's error text>" when the
availability tool returns a TOOL_ERROR; "no longer free" only when the query succeeded without the room. V07 does not
check public holidays (not implemented). Requester notes are replaced by `wrap_notes(...)`
before anything reaches state, are never parsed into requirements or sent in a brief (only an LLM planner's
`soft_preferences` may come from them; the officer's revise notes likewise reach the venue brief only as
"Officer revision (see soft_preferences)"), and the tool trace keeps only
whitelisted summaries (notes `"<omitted>"`). Checkpoints use SqliteSaver at `AGENT_CHECKPOINT_PATH` (default
`agent-service/data/checkpoints.sqlite`, git-ignored); never InMemorySaver outside tests. The trace (nodes, steps with
tool calls, validation `{attempt, rule, passed, message}`) lives in append-only state and is summaries, inputs, outputs
and timings only; step `sequence` and validation `attempt` never reset within a thread (one AgentRuns row per thread).
Each node binds its own `recording()` so tool calls attach to the right step. Limits (`app/limits.py`): MAX_REPLANS = 2,
MAX_DELEGATIONS = 3 × (1 + MAX_REPLANS) = 9 (deviation from plan §10.5's 6, which always stopped the second re-plan),
both counted per revision (an officer revise resets them); graph recursion_limit 40 (worst invocation 26 supersteps);
10 s per tool call; 180 s per run segment ("Run timed out"). Tool errors are `TOOL_ERROR:` strings: an HTTP 4xx is a
domain answer (worker reports unmet), `unavailable` (network, timeout, 401/403, 5xx) fails the step. A request-context
404 ends in safe_failure "Booking request not found"; a policy fetch failure in "policy unavailable". Tests use
`tests/fake_api.py` (seed-shaped fake of the 3.1 routes on `httpx.MockTransport`) and `tests/harness.py`; each test
gets its own temp checkpoint file; `-m live` runs against the real API only when `LIVE_*` env vars are set.

LLM agents (Phase 4, Tasks 4.1–4.3): `AGENT_LLM_AGENTS` is a comma list of agents that call Gemini (`supervisor`,
`venue_matching`, `equipment_allocation`; `policy_cost` is rejected at startup until 4.4 adds it to
`LLM_IMPLEMENTED` in `app/config.py`). Empty is the default and CI: every agent is a stub and nothing calls Gemini. Any
LLM agent makes `GOOGLE_API_KEY` required at startup (an empty value counts as missing; the error never contains the
key). Never print, log, echo or commit the key, or put it in a command line, test or fixture; check it by length only.
`app/llm.py` `build_chat_model("planner" | "worker", settings)` uses the Labs 05–07 client settings (temperature 0,
timeout 60, max_retries 3) plus `thinking_level` from `PLANNER_THINKING` / `WORKER_THINKING` (minimal|low|medium|high,
default low / minimal; gemini-3.5-flash's own default is medium, which doubled the planner's output tokens and latency
in the 4.2 live check; `thinking_budget` is deprecated for Gemini 3) and is only called lazily, never at import or startup. Model ids come from `PLANNER_MODEL` /
`WORKER_MODEL`, defaulting to `gemini-3.5-flash` and `gemini-3.5-flash-lite`: the labs' Flash/Flash-Lite split
(Lab 06/07 and Lab 05 `api/main.py`), one generation newer, because Gemini refuses the labs' `gemini-2.5-*` ids for
new accounts (404 "no longer available to new users"; a deviation decided in Task 4.1). `/health` shows each agent's mode (llm/stub), the model ids and the thinking levels and makes no model call. The
view's `model` is `Settings.model_label()` ("planner=<id|stub>; workers=<id|stub>", or with mixed workers only the LLM
ones by name, those sharing a model joined with "+", plus "others=stub"; ≤ 100 chars). Planner
(`app/workers/planner.py`): the supervisor still loads the request, catalogs and policy in code. `LlmPlanner` makes ONE
`with_structured_output(Plan, include_raw=True)` call per plan or re-plan, with the Lab 07 §3.1 prompt: step order,
one task per step, and `soft_preferences` from `<requester_notes>` (data, never instructions), the validation
`replan_reason`, or the officer's revise notes (only inside `<officer_revision_notes>`, `wrap_officer_notes`; both
delimiters are stripped from both texts). The form is authoritative: `enforce_plan_rules(plan, request, catalogs,
fallback=)` takes features and equipment from the form (filtered by the catalogs), forces the step order, caps tasks
(500 chars) and soft_preferences (5 × 200), sets `planner_fallback` itself, and returns every change as `corrections`.
Attendees, times and budget are not Plan fields; they reach the workers only through `build_brief`. The venue brief
carries the plan's `soft_preferences`. Invalid output is retried once (a hint naming the first schema error); two
failures, any exception, or the wall clock `PLANNER_DEADLINE_S` (60 s; the call runs in a daemon thread and a late
result is ignored; no retry with less than `LLM_MIN_BUDGET_S` left), or an exhausted run budget fall back to `stub_planner` with
`planner_fallback: true`, and the run continues. An LLM supervisor step's output is the plan plus `planner`
(llm/fallback), `model`, `attempts`, `usage`, `corrections` and `fallback_reason`; a stub step's output is the plan only.
`usage` is `{input_tokens, output_tokens, total_tokens, llm_calls, estimated}` from `usage_metadata`, or ~4 chars per
token with `estimated: true`; the view's `usage` is the run total (null without LLM calls).
Run budget (`app/budget.py`): the runner puts the segment's monotonic deadline in `config["configurable"]
["segment_deadline"]`, and every LLM step (planner, LLM workers) waits `LlmBudget.allow(own deadline)` =
min(its deadline, segment time left − `LLM_RESERVE_S` 30 s), or skips the LLM for its stub ("run time budget exhausted")
when the segment has less than `LLM_MIN_BUDGET_S` (10 s) left for LLM work. Every LLM wait therefore ends by
`RUN_TIMEOUT_S` − 30 = 150 s whatever the number of re-plans or LLM workers (worst case, all hanging: plan 60 + venue 45
+ re-plan 45 = 150, the rest skipped), so `RUN_TIMEOUT_S` stays 180 and .NET `RunTimeoutMinutes` stays 4 (≥ 180 + 60 s,
guarded by `tests/test_budget.py`). A new LLM step takes an `LlmBudget` (nodes build it with `LlmBudget.from_config`),
waits in a daemon thread (`app/workers/deadline.py` `submit`) and never blocks past `allow()`.
LLM workers share one loop, `ToolAgentWorker` (`app/workers/tool_agent.py`: build, per-attempt thread and recorder,
retry, deadline, budget, fallback); a subclass sets its name, label, prompt, schema and retry hint and implements `check`.
Venue Matching LLM worker (`app/workers/venue_llm.py`, `LlmVenueWorker`, passed to `build_graph(workers=...)` by
`main.py`; `WORKER_DEADLINE_S` 45): `create_agent` with ONLY `search_available_rooms` and `get_room_details`, the Lab 07
§3.1 `VENUE_PROMPT` (free rooms that fit, up to 3 ranked with a one-line reason; only tool-returned ids; no pricing or
equipment; the BRIEF JSON beats the instruction sentence; exact unmet constraint and stop) and
`response_format=ToolStrategy(VenueResult, handle_errors=False)` (not the bare class: AutoStrategy would use Gemini's
native JSON mode in production and the tool strategy for a fake, so tests would not run the production path; our retry
is the only one). Its only input is the task string (`recursion_limit` `WORKER_RECURSION_LIMIT` 12). Each attempt runs
in its own thread with its own `recording()`, merged into the venue step afterwards (a call an abandoned thread makes
later never reaches the trace); `seen_room_ids` (V12) comes along. Code checks (`check_options`, over this attempt's
ToolMessages via `observe_messages`): a room is free only if a search for the brief's exact window returned it; options
with an id no tool returned, not free, inactive, fewer seats than attendees, above ratio × attendees, missing a required
feature, excluded or listed twice are dropped, and code/name/capacity/building/features are replaced from the tool data,
each change in `corrections`. Invalid output (a schema error, no VenueResult, nothing valid left while fitting rooms were
returned, no options and no unmet, or an unmet without a complete search) gets one retry with a hint; two failures, an
exception, the deadline, the run budget or an `unavailable` tool fall back to the stub (run by `run_worker`; a tool still
down then fails the step as before). LLM step output = the VenueResult plus `mode` (llm/fallback), `model`, `attempts`,
`usage` (every model call of the loop), `corrections`, `worker_fallback` and `fallback_reason`; the state's `venue` is
the VenueResult only, and stub output is unchanged. `run_worker(name, task, budget=)` returns a `WorkerOutcome(result,
meta)`. Tests never call Gemini: `tests/fake_llm.py` `FakePlannerModel` (canned Plan, `INVALID`, an exception or
`Sleep`) through `Harness(..., planner=LlmPlanner(lambda: model, id))`, and `FakeToolModel` (a `BaseChatModel` with
scripted `search()`/`details()`/`answer()` turns, `Sleep` or an exception) through the REAL `create_agent` with
`Harness(..., workers={"venue_matching": LlmVenueWorker(lambda: model, id)})`; equipment tests use the
`check_stock()`/`substitutes()`/`allocation()` turns. `-m live_llm` makes the real calls
(planner, venue worker with and without a preference, equipment worker on the demo lines and with the mics short, a
thinking comparison), only with
`RUN_LIVE_LLM=1 uv run --env-file ../.env pytest -m live_llm -s`.
Equipment Allocation LLM worker (Task 4.3, `app/workers/equipment_llm.py`, `LlmEquipmentWorker`, a ToolAgentWorker;
`WORKER_DEADLINE_S` 45 inside the shared `LlmBudget`): `create_agent` with ONLY `check_equipment_availability` and
`get_substitutes`, the Lab 07 §3.1 `EQUIPMENT_PROMPT` (check every requested code for the brief's exact window first;
covered by a `room_features` feature → qty 0 `room_builtin`; else portable if available ≥ qty; else `get_substitutes`,
check them, and propose one only with ≥ qty available, with a `substitutions` reason; else an unmet entry starting with
the code; every line exactly once; never invent codes or change quantities; no room, pricing or policy decisions; the
BRIEF beats the sentence) and `ToolStrategy(EquipmentResult, handle_errors=False)`. The model decides how each line is
covered and writes the reasons; `check_allocation` (pure, over this attempt's ToolMessages via `observe_equipment`)
applies the stub's rules (`workers/equipment.py`): stock counts only from a check for the brief's exact window; a line
code must be requested, or a substitute that `get_substitutes` returned FOR that requested code (directional, named in
its substitutions entry), anything else is dropped; quantities are the requested ones (overwritten, substitutes too);
`room_builtin` only when the tool's `coveredByFeatureCode` is in the brief's room features (else invalid), and a
covered line proposed as portable becomes builtin; portable/substitute lines need enough stock (else invalid); an unmet
is invalid while the data shows the line could be met (covered, in stock, a substitute in stock) or its availability or
substitutes weren't checked, unless the window itself was refused (4xx); every requested code exactly once (else
invalid); the output is rebuilt in request order, each change in `corrections`. Retry, fallback and step output are the
venue worker's (label "Equipment"); `seen_equipment_codes` reach V12, V08 is unchanged, and a brief with no lines never
calls the model. Decisions (4.3): code owns the facts, the model chooses and explains; a slow run that finishes on stubs
(with the reason recorded) is the intended safe behaviour, so the budget and reserve stay; Flash-Lite ignores
temperature (fixed sampling), so worker runs are not fully deterministic, and the Phase 6 eval reports results over
repeated runs with a denominator (a known limitation).

Agent integration (Phase 3.3, `backend/CampusSpace.Api/Agents/`): only `IAgentClient` (typed HttpClient, base URL
`AgentService:BaseUrl`, X-Service-Key, 10 s timeout, snake_case JSON with string money read as decimal) calls the agent
service. It returns an outcome (Ok, AlreadyExists, NotFound, Conflict, Unavailable with a Detail such as "HTTP 401") and
never throws except for the caller's own cancellation; only GET is retried (at most twice), never a POST. A 400/401/403
is logged at Error ("check AgentService:ServiceKey"). Never log the key, a body or notes. Submit and retry-agent commit
the request change and a Queued run first, then start it best-effort through `IAgentRunStarter.TryStartAsync`, capped at
`AgentService:InlineStartTimeoutSeconds` (3) so a hung agent service never slows the 202; a run left Queued is the safety
net the poller starts. `AgentRunPoller` (BackgroundService, every `AgentService:PollSeconds`, off when
`AgentService:PollerEnabled` is false, as in Testing) processes Queued, Running and Resuming runs, each in its own scope
(`IAgentRunSync`); AwaitingApproval runs wait for the officer (Resuming: see Officer decisions). Mapping for a Running run:
running → copy new trace rows; awaiting_approval → copy the trace, run AwaitingApproval with Plan/Proposal/
PolicySnapshotJson, OfficerSummary, Model, Nodes, DurationMs, a Draft quote, request → PendingApproval; failed →
run Failed with the agent's error, request → AgentFailed with it in the history; completed/rejected/cancelled → Failed
"Unexpected agent status …"; 404 → Failed "Agent run not found (agent service state lost)"; Unavailable → nothing
(watchdog only). Watchdog (every tick, TimeProvider time): Running longer than `AgentService:RunTimeoutMinutes` (4) →
"Agent run timed out", Queued longer than `AgentService:StartTimeoutMinutes` (2, from CreatedAt) → "Agent service
unreachable"; either adds "(last error: …)" from the poller's in-memory last failure. The Draft quote is always .NET's:
proposal `equipment.lines` (`type_code`, `qty`, `source`) minus qty-0/room_builtin lines, codes resolved to type ids,
priced by `IQuotationCalculator` for the request's slot and requester role, saved with `CreateDraftAsync` and
`AgentRunId`; the agent's own quote is never trusted (V09 compared it). Lock order everywhere (`RowLocks`): the
requester's advisory lock (submit, retry-agent only), then the booking request row, then its agent run row, then
booking/item rows; after locking, re-check the run and request statuses and skip if something moved them. The trace
copy is idempotent: steps are inserted only for a Sequence not stored yet (with their tool calls), rules only for a new
(Attempt, RuleCode); names are cut to their column limits (AgentName/ToolName 50, Model 100, FailureReason 1000,
history reason 500). One AgentRuns row per LangGraph thread (Id = thread_id); RevisionNo is .NET's counter per request:
a new run (submit, retry-agent, a failed approval) gets max(RevisionNo) + 1, and a revise sets the SAME row's RevisionNo
to max(RevisionNo) + 1 (the same thread continues), while the Python
`revision` is informational only (this settles addendum Open question 6 for us). `POST
/api/booking-requests/{id}/retry-agent` (Facilities Officer) restarts an AgentFailed request, or a Submitted request
with no live run (seeded or legacy data), re-checking the requester's cap (409 "The requester already has …"); anything
else is 409 "Only a failed or not-yet-started request can be (re)started". Cancelling a PendingApproval request whose run is Resuming (an approval
being finished) is 409 "Approval in progress"; otherwise cancel marks
its AwaitingApproval run Cancelled in the cancel transaction (the Draft is voided with the other live quotes), then sends
resume "cancel" best-effort; .NET never resumes a Cancelled thread. Tests: the factory registers `FakeAgentClient`
(`factory.AgentClient`, default: starts accepted, runs read as running) and sets `AgentService:ServiceKey`; change its
delegates only on a factory of your own. Poller tests use `AgentPollerEnv` (isolated seeded factory, `MutableTimeProvider`
starting at the real now, `Poller.PollOnceAsync()`), and agent views come from `Fixtures/*.json`, verbatim 3.2 responses
generated with the agent-service harness (see `Fixtures/README.md`; regenerate after a contract change). Submit leaves a
request AgentProcessing, which can't be cancelled: use `AgentRunTestData.ToPendingApprovalAsync` or `ToAgentFailedAsync`,
and `BookingRequestTestData.InsertSubmittedAsync` for a request with no run. Isolated factories clear their Npgsql pool
on dispose, so many of them don't exhaust the container's connections.

Officer decisions (Phase 3.4, `ApprovalService`, `ApprovalFinalizer`): `POST /api/booking-requests/{id}/approve
{comment?}`, `/reject {reason}` and `/request-revision {notes}` are FacilitiesOfficer only (reason and notes required,
≤ 1000, blank is a 400; the full text is in `ApprovalDecisions.Comment`, history reasons are cut to 500). Unknown → 404;
not PendingApproval → 409 "Only a request pending approval can be decided (it is …)"; live run not AwaitingApproval
(a decision is being finished) → 409 "This proposal is already being decided". Lock order: request row, then
`RowLocks.LiveAgentRunAsync`. Every decision inserts its ApprovalDecisions row in the first transaction, before any
agent call; the saved decision is what approve and the poller act on, and a re-sent resume always uses it. Approve: tx1
decision + run → Resuming; resume "approve" (not accepted → 202 at once); then GET every `ApprovalPollMilliseconds` (500)
for up to `ApprovalWaitSeconds` (10), real time, not TimeProvider: completed → the finaliser → 200 with the Approved
detail; a failure → 409 with its message; still running → 202 `{requestId, status: "ApprovalInProgress"}` + Location,
and the poller finishes it. Reject (one tx): decision, run → Rejected + CompletedAt, live quotes voided, request →
Rejected (officer, reason), then best-effort resume "reject" → 200. Revise (one tx): decision, live quotes voided, the
same run → Resuming with RevisionNo = max + 1, request PendingApproval → RevisionRequested → AgentProcessing (officer,
notes on both rows), then resume "revise" with the notes → 202. `IApprovalFinalizer.FinalizeApprovedAsync(runId, view)`
is shared by approve and the poller and idempotent (it clears the change tracker, locks request then run, and acts only
while the run is Resuming, its latest decision is Approve and the request is PendingApproval). In one READ COMMITTED
tx it re-checks, then inserts the Booking (Confirmed, the request's range), `ReserveAsync`, `IssueForApprovalAsync` (the
Draft is issued when it equals the recomputed quote; otherwise it is voided and a new Issued quote added, with "Quote
recalculated at approval: Draft X, issued Y" in the history), request → Approved with the deciding officer as the actor,
run → Completed with the finalize trace. Approval re-check (our reading of addendum A.1/A.3, not a deviation): V05
`CheckSlot` and V06 `CheckTiming` with the CURRENT policy, but V06's lead time and advance window as of submission
(`CheckTiming(..., asOf)`, the Submitted history row's ChangedAt, else CreatedAt), plus "start is still in the future"
now; a requester who submitted on time mustn't fail because the officer was slow, but a policy change still applies.
Then the room is active, has no overlapping blackout (V07) or active booking (V02, friendly; `no_room_overlap` 23P01 is
the guarantee), and still has the feature covering each room_builtin line (addendum B, V08*); `ReserveAsync` is V08.
Failure kinds (our answer to addendum Open question 7, `ApprovalFailureKind`): every approval failure, whatever its
source (agent finalize failed, a .NET re-check, 23P01, `ReserveAsync`, the watchdog's "Agent did not confirm the approval
in time", an unexpected agent status or 404), is classified in ONE place, `FailApprovalAsync`, by running .NET's own time
checks first: `CheckSlot` (current policy), `CheckTiming` as of submission (current policy), start still in the future.
.NET is the authority; the original (often the agent's) reason is kept in the run's FailureReason for the trace. Any
fails → Time: run Failed, live quote voided, request PendingApproval → Rejected by the system (actor null, "The requested
time is no longer valid: <.NET's message>"), no new run, best-effort resume "cancel"; 409 "The requested time is no longer
valid: …. The request was closed; the requester can submit a new time." (an approve call that finds the poller got there
first rebuilds it from that history reason). Otherwise → Proposal (room busy/23P01, blackout, inactive room, equipment
short, builtin feature removed, agent finalize failed, unexpected status, not confirmed in time): run Failed, quote
voided, request → RevisionRequested with the original reason (saved, which frees the live-run index) → a new run
(RevisionNo next) → AgentProcessing in one tx, then
a best-effort start; 409 "The proposal is no longer valid: …. A new proposal is being prepared." Poller, Resuming (GET
first; the watchdog counts `RunTimeoutMinutes` from the decision's DecidedAt): awaiting_approval with no validation
attempt newer than stored → the resume was lost: re-send the saved decision (once per tick; timed out → fail); revise +
newer attempt → the 3.3 awaiting path (trace, new Draft, PolicySnapshotJson overwritten, AgentProcessing →
PendingApproval); running → copy the trace (timed out → fail); approve + completed → finaliser (even when timed out);
failed → approve: `FailApprovalAsync`, revise: run Failed + AgentFailed; any other status or 404 → warning, then the
same failure for its decision; timed out → approve: `FailApprovalAsync` "Agent did not confirm the approval in time",
revise: "Agent run timed out" + AgentFailed. Orphans: a live run whose request doesn't match (Queued/Running need AgentProcessing;
Resuming needs AgentProcessing for a revise or PendingApproval for an approve; no decision is an orphan) → run Failed
"Orphaned run (request is …)", the request untouched, best-effort resume "cancel" for Running/Resuming. A Failed run keeps
the view's Plan/Proposal/PolicySnapshotJson. Tests: `AgentPollerEnv.ToPendingApprovalAsync` (then the agent reads as the
completed fixture), `OfficerAsync`, `DecideAsync(id, action, body)`, `SetPolicyAsync`; the factory sets
`ApprovalWaitSeconds` 1 and `ApprovalPollMilliseconds` 50; the 3.4 fixtures are `agent-completed-*`,
`agent-revised-student` and `agent-finalize-failed`.

Approval UI (Phase 3.5): read endpoints, no new rules. `GET /api/approvals/queue` (FacilitiesOfficer; PendingApproval
only, `pendingSince` ascending by default, sort `pendingSince|requestedStart|attendees`, search purpose/requester/club),
`GET /api/booking-requests/{id}/agent-runs` (FacilitiesOfficer, newest first) and `GET /api/agent-runs/{id}`
(FacilitiesOfficer: typed proposal, steps with tool calls, validation grouped by attempt latest first, decisions, policy
snapshot, `policyChangedKeys`). Requesters get only `BookingRequestDetailDto.LatestProposal` (`IBookingRequestService`),
the same summary as officers: the live (Draft/Issued) quote's id, total and exempt flag, its run's id and RevisionNo, and the
room (the booking's once approved, else the proposal's room_id, code/name from Rooms); null without a live quote. Never the
trace, plan or policy (data minimisation); agent-runs are 403 for them. `ProposalMapper` (`Agents/`) takes the chosen room
from the proposal's `room_id`, never from the order of `venue.options` (no match: the room from Rooms, every option an
alternative, a warning); the agent's own quote is never exposed. `PolicyDiff.ChangedKeys` compares the run's snapshot with
`ToPublicValues()` per key (numbers by value, objects structurally, a missing key is changed), in `PolicyKeys.All` order.
`RequesterDto` carries `Role`. React (`features/approvals/`, Officer only via `ROUTE_ROLES`): the queue refetches every 15 s;
the detail shows the request, `ProposalCard` (source chips portable / built into the room / substitute), `QuoteTable`
(`features/quotations/`, the .NET quotation only), `ValidationChecklist`, `AgentTimeline` (JSON as `JSON.stringify`
text in `<pre>`), `RunHistory`, `PolicyChangedBanner` (labels from `policyLabel`). Decisions go through `DecisionDialog`
(RHF + Zod, ≤ 1000, reason/notes required) and show only while the request is PendingApproval and its newest run is
AwaitingApproval: approve 200 → toast and Approved; 202 ApprovalInProgress → "Approval in progress…" and the request
re-fetches every 3 s (`APPROVAL_IN_PROGRESS_REFRESH_MS`) until it leaves PendingApproval; revise 202 → "New proposal being
prepared" (follows `refreshIntervalFor`); 409 → a page alert with the Problem Details `title` exactly as sent (the API
puts every message there, never `detail`) via `useApiMutation`'s `onConflict`, then everything re-fetches. Run, run-detail
and quotation queries sit under `bookingRequestsKeys.all` and are keyed by the request's `updatedAt`, so a status change
reloads them. The request detail page links "Review proposal" (PendingApproval) and shows `RetryAgentButton` ("Retry
agent" for AgentFailed, "Start agent" for Submitted with no live run, `retryAgentLabel` in `approvals/agentRuns.ts`; a
409 toast is the server's title). Flutter's `RequestOutcomeCard`: PendingApproval "Proposed: <room>, LKR x" (or
Fee-exempt) + "Waiting for the Facilities Officer"; Approved the room, campus slot and `QuotationSection`
(`requestQuotationProvider`, only while Approved); Rejected "Rejected by Facilities", or "Closed automatically" when the
Rejected history row has no actor, with its reason; AgentFailed "We couldn't prepare a proposal: <reason>. Facilities can
retry."; after an officer's revise "Facilities asked for changes; a new proposal is being prepared" with the notes (a
system RevisionRequested, a failed approval, reads "The proposal is being prepared again"). Every reason is plain text.
Fixtures: web `features/approvals/approvalsFixtures.ts` and mobile `test/fixtures/proposals.dart` are verbatim API
captures (generated by the Task 3.5 capture scripts; recapture after a contract change). CI also runs the mobile request
tests with `TZ=America/New_York`.

If Docker Hub is unreachable, Testcontainers cannot pull its Ryuk reaper image. Run the tests with
`TESTCONTAINERS_RYUK_DISABLED=true` (local only; never commit it).

CI (`.github/workflows/ci.yml`, job `backend` runs restore, Release build with warnings as errors, and tests):

```bash
gh pr checks --watch                                # watch the current branch's PR run until it finishes
gh run list --limit 1                               # latest run and its URL
gh run view --log-failed                            # read the failing step's log
```

<!-- Add web/mobile/agent-service commands as each app is scaffolded. -->
