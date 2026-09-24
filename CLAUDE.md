# CampusSpace AI: SE3090 group project

CampusSpace AI books campus rooms and equipment. Requesters submit a booking objective from Flutter.
A LangGraph multi-agent service drafts a proposal. A Facilities Officer approves it in React, and .NET books it in one transaction.
Four students each own one business component (A–D) end-to-end, including one distinct agent.

**Source of truth:** `docs/CampusSpace_AI_Project_Plan.pdf` plus `docs/plan-addendum.md`. The addendum wins where they differ.
If a request conflicts with them, **stop and ask** before doing anything.

## Repo layout (§7.2)

```
backend/CampusSpace.Api/     Controllers/ Dtos/ Services/ Data/ (AppDbContext, Configurations/, Migrations/, Seed.cs)
                             Models/ Agents/ (AgentClient, AgentRunPoller) Internal/ (X-Agent-Key tool routes)
backend/CampusSpace.Tests/   xUnit unit + integration (Testcontainers)
web/                         React + TS + Vite
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
| Web | React 18 + TypeScript + Vite |
| Mobile | Flutter 3.47 stable (Android only) |
| Agent service | Python 3.11 via `uv` |

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

<!-- Add build/test commands as each app is scaffolded. -->
