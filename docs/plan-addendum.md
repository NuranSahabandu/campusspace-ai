# CampusSpace AI: Project Plan Addendum

This file amends `docs/CampusSpace_AI_Project_Plan.pdf`. Together they are the source of truth.
Where the two disagree, this addendum wins. Section numbers (§) refer to the plan PDF.

| # | Change | Component | Status |
|---|--------|-----------|--------|
| A | `PolicySettings` table: booking policy moves out of code and into data | D (Approval, Pricing and Analytics) | Approved |
| B | `EquipmentTypes.CoveredByFeatureCode`: some portable equipment is covered by a room's built-in feature | B (Equipment) | Approved |

Items marked **(proposed)** are details I filled in to make the approved change work. They still
need team review. Items under "Open questions" need a decision before implementation.

---

## Change A: PolicySettings table (Component D)

### A.1 Decision

**Table `PolicySettings`**

| Column | Type | Constraints |
|--------|------|-------------|
| `Key` | text | PK |
| `Value` | text | NOT NULL |
| `ValueType` | text | NOT NULL, CHECK IN (`'int'`, `'decimal'`, `'bool'`, `'json'`) |
| `Description` | text | Human-readable meaning shown on the policy page |
| `UpdatedById` | bigint | FK → `Users.Id`, NULL (NULL for seeded rows) |
| `UpdatedAt` | timestamptz | UTC |

**Seed values** (these come from the plan: §3.2, §8.3, §10.8 V03/V05/V06/V11)

| Key | ValueType | Value | Replaces in plan |
|-----|-----------|-------|------------------|
| `opening_hours` | json | Mon–Fri 08:00–20:00, Sat 08:00–16:00, Sun closed | V05 |
| `min_lead_time_hours` | int | 48 | V06 |
| `max_advance_days_student` | int | 60 | V06, §3.2 |
| `max_advance_days_lecturer` | int | 90 | V06, §3.2 |
| `max_duration_hours` | int | 8 | V05 |
| `max_capacity_ratio` | decimal (proposed) | 3 | V03 |
| `slot_granularity_minutes` | int | 30 | V05 |
| `free_cancellation_hours` | int | 24 | §8.3 cancellation policy |
| `max_open_requests` | int | 3 | V11 |

Proposed JSON shape for `opening_hours` (times are campus local time, see Open question 3):

```json
{"mon":{"open":"08:00","close":"20:00"},"tue":{"open":"08:00","close":"20:00"},
 "wed":{"open":"08:00","close":"20:00"},"thu":{"open":"08:00","close":"20:00"},
 "fri":{"open":"08:00","close":"20:00"},"sat":{"open":"08:00","close":"16:00"},"sun":null}
```

**Editing.** The Facilities Officer edits the settings on a React "Booking policy" page through
`GET /api/policy-settings` and `PUT /api/policy-settings`. Both are restricted to `[Authorize(Roles = "FacilitiesOfficer")]`.
`PUT` checks each value against its `ValueType`. An invalid value returns 400 Problem Details
with field errors. Each changed key writes one `AuditLogs` row with `EntityType = "PolicySetting"`,
`EntityId = Key`, and the old and new values in `DetailsJson`. The row also sets `UpdatedById` and `UpdatedAt`.

**One source of truth.** No policy number may be hard-coded anywhere: not in C#, Python, TypeScript,
Dart, prompts, Zod schemas or data annotations.

| Consumer | How it reads policy |
|----------|--------------------|
| .NET approval re-check (critical rules V05, V06) | `IPolicySettingsService`, CURRENT values at approval time |
| .NET cancellation policy | `IPolicySettingsService` (`free_cancellation_hours`) |
| .NET eligibility checks at submission | `IPolicySettingsService` (`max_open_requests`) |
| `GET /internal/agent-tools/policy` | `IPolicySettingsService` (the endpoint only exposes it) |
| Python agent service | ONLY `GET /internal/agent-tools/policy` with `X-Agent-Key`. It never reads the DB. |
| Policy and Cost agent + Python validator (V03, V05, V06, V11) | One snapshot taken at run start, stored in graph state |
| `AgentRuns` | The snapshot is stored with the run, so the audit trail shows which values the agents used |

### A.2 Proposed implementation details

- **`IPolicySettingsService`** (D owns it): `GetSnapshotAsync()` returns a typed `PolicySnapshot` record.
  `UpdateAsync(changes, officerId)` validates the changes and writes them together with the audit rows in one transaction.
  Registered with `AddScoped`. The service should read the database on each call. That way the
  approval always sees current values. Caching is not worth the invalidation risk here.
- **`PUT` body**: `{ "settings": [ { "key": "...", "value": "..." } ] }`. PUT only updates existing keys.
  An unknown key returns 400. Keys cannot be created or deleted through the API. Keys come only from seed data and migrations.
- **Data annotations cannot carry these values.** `[Range]` arguments are compile-time constants.
  Policy checks therefore run in the service layer and return 400 Problem Details. DTO annotations keep only
  structural rules, such as Required, Attendees > 0 and end > start.
- **Snapshot capture**: `_run()` in the agent service calls `GET /internal/agent-tools/policy`
  once on a fresh start. It does not call it again on a `Command(resume=...)`. It puts the result in a new state key,
  `policy: dict`. This adds no graph node, so the trajectory in Appendix B does not change. The checkpointer
  keeps the snapshot while the run is paused at the gate.
- **Storing the snapshot**: `GET /workflows/{thread_id}` returns `policy_snapshot`. `AgentRunPoller` writes it
  to a new column, `AgentRuns.PolicySnapshotJson jsonb`. The snapshot has to come from Python because
  Python's copy is the one the agents actually used.
- **If the fetch fails**, the run goes to safe failure with the reason "policy unavailable". There is no fallback to defaults,
  because defaults in code would be hard-coded policy.

### A.3 What Change A affects in the plan

**Sections**

| § | Effect |
|---|--------|
| §3.2 User roles | "90 days vs 60" becomes the seeded defaults of `max_advance_days_lecturer` / `_student` |
| §4 Use cases | Add **UC29** Facilities Officer: manage booking policy settings (Comp. D) |
| §5 Components | D gains policy settings management. A lists "operating hours", but opening hours now live in D's `PolicySettings` (see Open question 4) |
| §7.1 Rule 3 | No change. This change follows the rule: the agent has no DB access and reads through a read-only tool route |
| §8 intro conventions | `PolicySettings` departs from the conventions on purpose: natural text PK instead of IDENTITY, and `UpdatedAt` with no `CreatedAt`. The automatic timestamping in `SaveChangesAsync` must handle this. Record the reason in the report |
| §8.1 Component D tables | New `PolicySettings` table. Agent workflow tables: add `AgentRuns.PolicySnapshotJson jsonb` (proposed) |
| §8.3 Status machine | "Free more than 24 hours before the start" becomes `free_cancellation_hours` |
| §8.4 Seed data | Seed the 9 rows above only when the table is empty (Lab 04 pattern) |
| §9 REST API | New endpoints; changed contracts (see the Endpoints table below) |
| §10.3 Shared state | Add `policy: dict` to `State` |
| §10.4 Agents | Policy and Cost's `check_policy` reads the snapshot (see Agents below) |
| §10.8 Validation | V03, V05, V06, V11 read the snapshot instead of literals |
| §10.12 .NET integration | Poller persists `policy_snapshot` |
| §11 Walkthrough | Step 2: .NET checks V11 eligibility using the current `max_open_requests`. The Sunday failure path still works, because Sunday is closed in the seeded `opening_hours` |
| §12 React | New "Booking policy" screen; approval detail changes |
| §13 Flutter | New request stepper and cancel dialog must not hard-code limits |
| §15.3 Audit | Every policy change writes an AuditLogs row |
| §16.1 Testing | New tests (see list below) |
| §16.2 Agent eval | Golden cases use an explicit policy snapshot fixture. Case 6 (Sunday) relies on `opening_hours` |
| §18 Ownership | D owns the table, service, endpoints and React page. A, C and the validator are consumers |
| §19 Build order | Phase 1: entity, migration, seed, service. Phase 2: cancellation and eligibility read the service. Phase 3: the internal policy route and snapshot in state. Phase 5: React page |
| §20 ADRs | Add **ADR-9 Policy as data**: DB table + snapshot per run + re-check against current values. Alternatives: appsettings.json, constants |
| §21 Report | Database section: new table and its deviations from convention |
| §22 Viva | "Change one rule live (3× → 2×, 48 h → 24 h)" now means editing it on the Booking policy page, starting a run, and showing the changed result. Tests must pass in the policy value, not import a constant |
| App. A.2 | The initial state in `start()` gains `policy`, filled by `_run()` before `GRAPH.stream` |
| App. A.3 | `RevalidateCriticalRules` reads V05/V06 values from `IPolicySettingsService` (current values, not the snapshot) |

**Validation rules**

| Rule | Settings used | Where it runs |
|------|---------------|---------------|
| V03 | `max_capacity_ratio` | Python validator (snapshot) |
| V05* | `opening_hours`, `max_duration_hours`, `slot_granularity_minutes` | Python validator (snapshot) + .NET approval (current) |
| V06* | `min_lead_time_hours`, `max_advance_days_student`, `max_advance_days_lecturer` (chosen by requester role) | Python validator (snapshot) + .NET approval (current) |
| V11 | `max_open_requests` | .NET eligibility at submission (current) + Python validator (snapshot) |
| Cancellation (not a V rule) | `free_cancellation_hours` | .NET `POST .../cancel` (current) |

**Endpoints**

| Endpoint | Change |
|----------|--------|
| `GET /api/policy-settings` | **New.** Officer only. Returns every key, value, type, description, updatedBy and updatedAt |
| `PUT /api/policy-settings` | **New.** Officer only. Validates each value against its ValueType, writes the audit rows, returns 200 with the updated list |
| `GET /internal/agent-tools/policy` | Now returns the full snapshot from `IPolicySettingsService`. Called once per run at start. The Policy and Cost agent no longer calls it per tool call |
| `GET /internal/agent-tools/request-context/{id}` | No change. It still returns the open-request count. The comparison uses the snapshot |
| `POST /api/booking-requests` | Eligibility check reads `max_open_requests` |
| `POST /api/booking-requests/{id}/cancel` | Late vs free cancellation is decided by `free_cancellation_hours` |
| `POST /api/booking-requests/{id}/approve` | V05/V06 re-check against current values |
| `GET /api/rooms/{id}/schedule` | If it shows open hours or slot boundaries, derive them from `opening_hours` and `slot_granularity_minutes` |
| `GET /api/agent-runs/{id}` | Includes the policy snapshot |
| Python `GET /workflows/{thread_id}` | Response gains `policy_snapshot` |
| Python `POST /workflows` | `_run()` fetches the snapshot before streaming |

**Screens**

| Screen | Change |
|--------|--------|
| React: Booking policy (new, D) | Table of settings with an editor per ValueType (number input, toggle, opening-hours grid for json). Validation errors come from the server. `<ProtectedRoute roles={["FacilitiesOfficer"]}>` plus a nav item |
| React: Approval detail (D) | Validation checklist messages show the values used. If the current policy differs from the run's snapshot, show a warning ("policy changed since this proposal") |
| React: Agent runs monitor (C/D) | The run detail shows the policy snapshot |
| React: Audit log (Shared) | Shows `PolicySetting` changes (no code change if generic) |
| Flutter: New request stepper (C) | Date and time pickers must not hard-code lead time, advance window, granularity or hours (see Open question 1) |
| Flutter: My requests: cancel confirmation (C) | Any "free until X hours" text must come from the server, not a literal |
| Flutter: Room detail schedule (A) | Slots come from the server |

**Agents**

| Agent | Change |
|-------|--------|
| Policy and Cost (D) | `check_policy` returns facts computed from `state["policy"]`, not a live call. Its prompt must not contain policy numbers. See Open question 2 for how a tool reaches state under context isolation |
| Supervisor (C) | Owns the graph skeleton, so `_run()`'s snapshot fetch and the `policy` state key belong to C |
| Venue Matching (A) | Its prompt says "fit to attendee count". If the task brief mentions the capacity ratio, the value must come from the snapshot, not the prompt text |
| Validator node (not an LLM agent) | V03/V05/V06/V11 take `state["policy"]` as a parameter, which keeps them pure functions for pytest |
| Equipment Allocation (B) | No change |

**Tests to add** (§16.1): PUT rejects a bad value for each ValueType (400); a non-officer gets 403 on GET and PUT;
PUT writes one audit row per changed key; the approval re-check uses current values when they differ from
the snapshot; cancellation honours a changed `free_cancellation_hours`; eligibility honours a changed
`max_open_requests`; pytest for each V rule with two different snapshots; the snapshot survives
interrupt and resume.

---

## Change B: EquipmentTypes.CoveredByFeatureCode (Component B)

### B.1 Decision

- New column `EquipmentTypes.CoveredByFeatureCode text NULL`, FK → `Features.Code` (already UNIQUE).
- Example seed: `PROJ-PORTABLE.CoveredByFeatureCode = 'projector'`.
- It is set through a select on the React equipment-type form.
- The Equipment Allocation agent drops a requested line when the chosen room has the covering feature.
  It outputs `qty: 0, source: "room_builtin"`. Such lines are **not priced** and **do not count against V08**.

### B.2 Proposed implementation details

- EF Core: FK to an alternate key with `HasPrincipalKey(f => f.Code)`. Index the FK column (§8 convention).
  `ON DELETE RESTRICT` (§8 default), so a feature cannot be deleted while an equipment type references it.
  Editing a `Features.Code` that is referenced should be blocked (409) rather than cascaded.
- Cross-component FK: B's table references A's `Features`. The migration order must create `Features` first.
- **Make the drop deterministic.** After the LLM worker returns, a code step (like `enforce_plan_rules`)
  compares each line's `covered_by_feature_code` with the chosen room's feature codes and forces
  `qty 0 / room_builtin`. The LLM must never be the only thing deciding a price-affecting drop (§10.8: "the LLM never decides whether something is valid").
- `EquipmentResult.lines[].source` becomes `Literal["portable", "room_builtin", "substitute"]` so that V01 rejects anything else.
- The **chosen room** is `venue.options[0]`. It reaches the worker through the supervisor's task brief
  as a room id plus feature codes ("IDs, not whole records", §10.5).
- The requester's `RequestedEquipmentLines` row is unchanged (Quantity > 0). Only the proposal line becomes qty 0.

### B.3 What Change B affects in the plan

**Sections**

| § | Effect |
|---|--------|
| §5 Components | B's equipment types now link to A's features |
| §8.1 Component B tables | `EquipmentTypes` gains `CoveredByFeatureCode` (FK → `Features.Code`, NULL, indexed) |
| §8.4 Seed data | Set `PROJ-PORTABLE → projector`. Decide whether other types (for example a portable speaker → `sound_system`) are covered too |
| §9 Component B | The equipment-types DTOs gain the field. Validation: the code must exist in `Features`, otherwise 400 |
| §9 Internal routes | `catalog/equipment` and `equipment/availability` return `covered_by_feature_code` |
| §10.4 Agents | Equipment Allocation's existing "drop what the chosen room has built in" now has a defined mechanism and output |
| §10.5 Supervisor | Task brief for equipment_allocation must include the chosen room id and its feature codes. So equipment still runs after venue (`enforce_plan_rules` already puts venue first) |
| §10.8 Validation | V01, V08, V09, V10 (see below) |
| §10.9 Finalize | The V08 re-run skips `room_builtin` lines, but checks that the room still has the covering feature |
| §11 Walkthrough | Step 4 ("drops the portable projector because A301 has one") depends on this. **Inconsistency:** step 1's request lists only 2 wireless mics, with no PROJ-PORTABLE, so there is nothing to drop. Step 1 should add "1 × portable projector" |
| §12 React | Equipment type form |
| §16.1 Testing | New tests (see list below) |
| §16.2 Agent eval | Golden case 1's `request.equipment` lacks PROJ-PORTABLE (same inconsistency). Add it and expect a `room_builtin` line. Add a case where the chosen room lacks the feature, so the line stays portable and priced |
| §19 Build order | Phase 1: column, migration, seed, form. Phase 3: the stub equipment worker emits a `room_builtin` line. Phase 4: the deterministic drop step |
| App. A.3 | `AddEquipmentReservations` must skip qty-0 lines, because `EquipmentReservations.Quantity CHECK > 0` would reject them. `IssueQuotation` must skip them too |
| App. B | The sample payload already matches (`PROJ-PORTABLE`, qty 0, `room_builtin`). No change |

**Validation rules**

| Rule | Effect |
|------|--------|
| V01 | Schema accepts `source = "room_builtin"` only with `qty = 0` |
| V08* | Ignores `room_builtin` lines. At approval, .NET confirms that the room still has the covering feature. If the feature was removed, the line is uncovered and V08 fails |
| V09* | .NET's recomputed total excludes `room_builtin` lines. Both sides must agree |
| V10 | Budget comparison uses the total without builtin lines (follows from V09) |
| V12 | No change. Type codes are not IDs from tool results |

**Endpoints**

| Endpoint | Change |
|----------|--------|
| `GET/POST/PUT /api/equipment-types` | DTOs gain `coveredByFeatureCode` (nullable). POST/PUT return 400 for an unknown code |
| `GET /api/features` | Used by the new select (already exists; Officer) |
| `GET /internal/agent-tools/catalog/equipment` | Includes `covered_by_feature_code` |
| `GET /internal/agent-tools/equipment/availability` | Includes `covered_by_feature_code` for the type |
| `POST /internal/agent-tools/quote` | Ignores or rejects qty-0 `room_builtin` lines |
| `POST /api/booking-requests/{id}/approve` | Reservations and quotation lines skip builtin lines. The V08 re-check verifies the covering feature |
| `GET /api/loans/today` | No change (builtin lines create no reservation, so there is nothing to hand over) |

**Screens**

| Screen | Change |
|--------|--------|
| React: Equipment types form (B) | "Covered by room feature" select, filled from `GET /api/features`, with an empty option |
| React: Equipment types list (B) | Optional column showing the covering feature |
| React: Approval detail (D) | Equipment lines show builtin items as "Provided by room", unpriced |
| Flutter: Request detail / quotation view (C/D) | Builtin lines, if shown, are labelled and have no price |
| Flutter: New request stepper (C) | No change required |

**Agents**

| Agent | Change |
|-------|--------|
| Equipment Allocation (B) | Receives the chosen room's feature codes in its brief. Outputs `qty 0 / room_builtin` lines. The prompt says covered lines need no availability check. The deterministic post-step enforces the drop |
| Supervisor (C) | The equipment_allocation task brief includes room id and feature codes. On a re-plan that changes the room, equipment allocation must run again |
| Policy and Cost (D) | `calculate_quote` excludes `room_builtin` lines |
| Venue Matching (A) | No change |

**Tests to add** (§16.1): the FK rejects an unknown feature code (DB and API 400); deleting a referenced
feature is blocked; pytest for the drop step (room has the feature → builtin; room lacks it → portable);
quote excludes builtin lines; V08 ignores builtin lines; the approval transaction creates no reservation for a
builtin line; approval fails V08 if the room lost the feature after the proposal.

---

## Open questions for review

1. **Flutter needs policy values, but `GET /api/policy-settings` is Officer-only.** Options:
   (a) add a read-only `GET /api/policy-settings/public` for any authenticated user, or
   (b) Flutter enforces no limits and relies on server errors. Option (b) weakens the UX.
2. **How does `check_policy` reach the snapshot?** Workers only get a task string (context isolation, §10.6).
   Options: build the Policy and Cost worker's tools per run with the snapshot bound in a closure, or
   include the relevant values in the task brief.
3. **Timezone of `opening_hours`.** Timestamps are UTC (§8). The hours are campus local time (the samples use +05:30).
   Is the timezone a setting, a config value, or fixed?
4. **Component A lists "operating hours"** (§5). Does A give up that feature to D's `PolicySettings`, or does A keep
   per-room hours that must sit inside the global hours?
5. **Scope of "no hard-coded policy numbers".** Do these stay as code constants, or become settings?
   - The checkout window: 30 minutes before the start (§9, Component B).
   - Agent runtime limits: `MAX_DELEGATIONS`, `MAX_REPLANS`, the 4-minute watchdog. §10.10 says these are "hard limits in code".
6. **Revise within the same run.** Should a revision keep the run-start snapshot or take a fresh one?
   A related question: is a revision a new `AgentRuns` row? `RevisionNo` suggests it is the same thread.
7. **Policy tightened after a proposal.** If V05/V06 then fail at approval, a revision cannot fix it, because
   times come from the form. Should approval move the request to a terminal state with a clear reason?
8. **Semantic limits on PUT.** Beyond the type check, should PUT enforce ranges? Examples: the ratio must be ≥ 1, granularity must divide 60,
   and `opening_hours` must have `close > open`.
9. **`Description` nullability.** The spec lists no NOT NULL. §8's default suggests NOT NULL.
