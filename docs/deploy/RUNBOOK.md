# Deployment runbook (Task 6.D3)

Neon (PostgreSQL 16) + Render (API + agent service, Docker, free) + Vercel (React). You create the accounts, click
through the dashboards and paste the secrets. Nothing in this runbook asks you to put a secret in Git, in a file, in a
command line or in a chat. Paste each secret only where a step says to.

```
 Browser ──HTTPS──► Vercel (React, static)              Flutter APK (D4)
    │                                                       │
    └─────────HTTPS + JWT──► Render: campusspace-api ◄──────┘
                              │   ▲
          X-Service-Key ──────┘   └────── X-Agent-Key (/internal/agent-tools, read-only)
                              ▼   │
                         Render: campusspace-agent ──HTTPS──► Gemini
                              │
   campusspace-api ── DIRECT endpoint, role campusspace_app (DML only) ──► Neon: database campusspace
   campusspace-agent ─ POOLED endpoint, role campusspace_agent ─────────► Neon: database campusspace_agent
   your Mac (migrations, roles) ── DIRECT endpoint, owner role ─────────► Neon: both
```

Which endpoint and why:

- **API → direct endpoint.** The API takes only transaction-scoped locks (`pg_advisory_xact_lock`, `SELECT … FOR UPDATE`
  inside transactions) and no session state, so pgbouncer's transaction pooling would also work. It is one long-lived
  process with its own Npgsql pool (`Maximum Pool Size=10`), so a second pooler adds nothing, and the direct endpoint
  avoids pgbouncer's reset and prepared-statement rules altogether.
- **Agent service → pooled endpoint** (`-pooler` in the host): its psycopg pool is already set up for it
  (`prepare_threshold=None`, D2).
- **Migrations and role SQL → direct endpoint**, as the owner role (DDL; `neon-db.sh` refuses a `-pooler` host).

Regions: Render **Singapore** (Render's regions are Oregon, Ohio, Virginia, Frankfurt and Singapore) and Neon **AWS Asia
Pacific (Singapore), `aws-ap-southeast-1`** (Neon has no region in India). Both are the closest to Sri Lanka, and next
to each other.

## 0. Before you start

- The `feat/deploy` PR is merged: Render and Vercel deploy from `main`.
- Docker Desktop is running (the scripts run `psql` from the `postgres:16` image, so you don't need psql installed).
- `dotnet ef` 8 is installed (`dotnet tool install -g dotnet-ef --version "8.*"`), and `jq` (macOS has it).
- A password manager entry (for example the macOS **Passwords** app) called "CampusSpace deploy". Every generated
  secret below goes there and nowhere else. Generate each one with the command shown; it lands on your clipboard
  (`pbcopy`), not on the screen.
- Accounts: Neon, Render and Vercel (sign in with GitHub), Cloudflare (R2), Google AI Studio (Gemini key), Brevo
  (optional, for email).

## 1. Neon

1. **Create the project.** Neon console → **New project**. Name `campusspace`, Postgres version **16**, cloud **AWS**,
   region **Asia Pacific (Singapore)**. Create.
   *Expected:* the project dashboard, branch `main`, a database `neondb` and the owner role `neondb_owner`.
2. **Fix the compute at 0.25 CU.** **Branches** → `main` → **Computes** → the primary compute → **Edit**. Set the
   autoscaling minimum **and** maximum to **0.25 CU** (do not allow 0.5). Leave scale to zero at 5 minutes (the Free
   plan can't change it). Save.
   *Expected:* the compute shows "0.25 CU".
3. **Copy the owner's DIRECT connection URL.** **Connect** (top of the dashboard) → branch `main`, database `neondb`,
   role `neondb_owner`, **Connection pooling OFF** → copy the `postgresql://…` URL. It must NOT contain `-pooler`.
   Keep it on the clipboard only; you paste it into the scripts' hidden prompt (steps 1.4, 1.5, 2).
4. **Create the databases, roles and grants.**
   ```bash
   ./scripts/neon-db.sh roles
   ```
   Paste the owner URL at `Owner connection URL (DIRECT endpoint; input hidden):`.
   *Expected* (NOTICE lines on a re-run are normal):
   ```
   roles and databases ready. Next: ./scripts/neon-db.sh password campusspace_app (and campusspace_agent)
          role        | has_any_attribute | member_of | connect_campusspace | connect_agent_db | create_in_public
   campusspace_agent  | f                 | -         | f                   | t                | f
   campusspace_app    | f                 | -         | t                   | f                | f
   migrations applied: none yet (run: ./scripts/neon-db.sh migrate)
   ```
   The roles are created by SQL (`docs/deploy/neon-roles.sql`), never in the console: a console-created role joins
   `neon_superuser`, which can read and write every table ([Neon: Manage roles](https://neon.com/docs/manage/roles)).
   The app role can also connect to Neon's default `neondb`, which is empty and where it can't create anything.
5. **Set the two role passwords** (one at a time):
   ```bash
   openssl rand -hex 32 | tr -d '\n' | pbcopy       # 64 hex characters (Neon requires >= 60 bits of entropy)
   ```
   Paste it into your password manager as "Neon campusspace_app". Then copy the owner URL again and run:
   ```bash
   ./scripts/neon-db.sh password campusspace_app
   ```
   Paste the owner URL, then paste the new password at `Enter new password for user "campusspace_app":` and again
   at `Enter it again:`. *Expected:* `password set for campusspace_app`. Repeat both commands for `campusspace_agent`
   ("Neon campusspace_agent").
   `\password` hashes the password in psql before it is sent. **If Neon refuses it** (an error about the password),
   use the fallback, which sends it as SQL over the TLS connection instead: `./scripts/neon-db.sh password-sql <role>`.
6. **Build the two connection strings** (in the password manager, not in a file). Take the endpoint host from the
   owner URL, e.g. `ep-cool-name-123456.ap-southeast-1.aws.neon.tech` (the pooled host inserts `-pooler` after the
   endpoint id: `ep-cool-name-123456-pooler.ap-southeast-1.aws.neon.tech`).
   - **API** (`ConnectionStrings__Default`; Npgsql keyword form, DIRECT host):
     ```
     Host=<endpoint>.ap-southeast-1.aws.neon.tech;Database=campusspace;Username=campusspace_app;Password=<app password>;SslMode=VerifyFull;Maximum Pool Size=10;Connection Idle Lifetime=60
     ```
     `VerifyFull` checks Neon's certificate against the image's CA store (startup refuses anything below `Require`).
     `Connection Idle Lifetime=60` closes idle connections after a minute, so Neon can scale to zero.
   - **Agent service** (`AGENT_CHECKPOINT_URL`; POOLED host):
     ```
     postgresql://campusspace_agent:<agent password>@<endpoint>-pooler.ap-southeast-1.aws.neon.tech/campusspace_agent?sslmode=require&channel_binding=require
     ```
   The hex passwords need no URL escaping.

## 2. Migrations

```bash
./scripts/neon-db.sh migrate
```
It first generates the idempotent EF script (`dotnet ef migrations script --idempotent`, into a temporary file with
no secrets, deleted afterwards), then asks for the owner URL (copy it again), applies it to `campusspace` and prints
the report.
*Expected:*
```
migrations applied to campusspace
 business_tables | app_can_insert
              29 |             29
 migrations_applied |           latest_migration
                 16 | 20261001090554_PhotosInObjectStorage
```
Running it again changes nothing. Later: a PR that adds a migration must be followed by `./scripts/neon-db.sh migrate`
**before** its API deploy, because the API (DML rights only) refuses to start with a pending migration; Render then
keeps the previous deploy running. New tables get the app role's rights automatically (default privileges).

## 3. Cloudflare R2 (damage photos)

1. Cloudflare dashboard → **R2 Object Storage** → **Create bucket**: name `campusspace-photos`, location hint
   **Asia-Pacific**. Leave **Public access** disabled (the API streams photos itself).
2. **R2** → **Manage API tokens** → **Create API token**: permission **Object Read & Write**, **Apply to specific
   buckets only** → `campusspace-photos`, TTL **forever** (or at least past 21 Oct 2026). Create.
3. Copy into the password manager: **Access Key ID**, **Secret Access Key**, and your **Account ID** (R2 overview page).
   The secret is shown once.

## 4. Render (API + agent service)

1. Render dashboard → **New** → **Blueprint** → connect the GitHub repository → branch `main` → Render reads
   `render.yaml` and lists the env group `campusspace-service-keys` and the services `campusspace-api` and
   `campusspace-agent` (free, Singapore, Docker).
2. Render asks for every `sync: false` variable. Fill them in (paste from the password manager):

   | Service | Key | Value / where it comes from |
   |---------|-----|-----------------------------|
   | campusspace-api | `ConnectionStrings__Default` | step 1.6, the API string (DIRECT, `campusspace_app`) |
   | campusspace-api | `Seed__DemoPassword` | `openssl rand -hex 8 \| tr -d '\n' \| pbcopy`: 16 characters (≥ 12, the seed's rule), digits and a–f only, so it is easy to type on a phone; 64 bits. Save it as "CampusSpace demo password": it goes in the submitted report, never in the README. **Choose it once**: the seed only adds missing accounts and never changes an existing password. |
   | campusspace-api | `Cors__AllowedOrigins__0` | `http://localhost:5173` for now (the API refuses to start with no origin); step 5 replaces it with the Vercel URL |
   | campusspace-api | `R2__AccountId` | step 3, Account ID |
   | campusspace-api | `R2__AccessKeyId` | step 3, Access Key ID |
   | campusspace-api | `R2__SecretAccessKey` | step 3, Secret Access Key |
   | campusspace-api | `R2__Bucket` | `campusspace-photos` |
   | campusspace-api | `Email__BrevoApiKey` | Brevo → SMTP & API → API keys → generate; or leave empty (every email is then recorded as Skipped) |
   | campusspace-api | `Email__FromAddress` | a sender verified in Brevo (empty without a key) |
   | campusspace-api | `Email__RedirectAllTo` | your own inbox. **Required with a Brevo key**: the seeded addresses are fake |
   | campusspace-agent | `GOOGLE_API_KEY` | Google AI Studio → Get API key |
   | campusspace-agent | `AGENT_CHECKPOINT_URL` | step 1.6, the agent string (POOLED, `campusspace_agent`) |

   Set by the Blueprint itself (nothing to paste): `ASPNETCORE_ENVIRONMENT=Production`, `Jwt__Issuer`,
   `Jwt__Audience`, `Cors__AllowedOrigins__1=http://localhost:5173`, `AgentService__BaseUrl`, `API_BASE_URL`,
   `AGENT_LLM_AGENTS` (all four agents). **Generated by Render** (random 256-bit, base64, 44 characters; created once
   and kept on later Blueprint syncs; you never need to see them): `AgentTools__Key` and `AgentService__ServiceKey` in
   the shared env group (both services read these exact names; two different values), and `Jwt__Key` on the API only,
   so the agent service can't sign user tokens. Both services check at startup that each key is ≥ 32 and that the two
   service keys differ.
3. **Apply**. *Expected:* two builds (the first takes several minutes), then both services **Live**. Render's health
   check is `/health/live` (no database or agent call).
4. **Check the real URLs.** Each service's page shows its URL. If it is not exactly
   `https://campusspace-api.onrender.com` / `https://campusspace-agent.onrender.com` (Render adds a suffix when a name
   is taken):
   - campusspace-api → **Environment** → `AgentService__BaseUrl` = the agent's real URL → **Save, rebuild, and deploy**;
   - campusspace-agent → **Environment** → `API_BASE_URL` = the API's real URL → **Save, rebuild, and deploy**;
   - and change the same two values in `render.yaml` in a small PR, or the next Blueprint sync puts the old ones back.
   Step 6's `/health` check on the API (`agent-service: Healthy`) proves the API reaches the agent.
5. **Auto-deploy.** Each service → **Settings** → **Build & Deploy** → **Auto-Deploy** shows "After CI checks pass"
   (`autoDeployTrigger: checksPass`): a push to `main` deploys only after GitHub CI is green, and only the service whose
   folder changed (`buildFilter`).

## 5. Vercel (React)

1. Vercel → **Add New…** → **Project** → import the GitHub repository.
2. **Root Directory**: `web`. Framework preset: **Vite** (detected). Build command `npm run build`, output `dist`.
3. **Environment Variables**: `VITE_API_URL` = the API's URL from step 4.4 (e.g. `https://campusspace-api.onrender.com`,
   no trailing slash), for **Production** (and Preview if you want). It is public (it is compiled into the bundle);
   never put a secret in a `VITE_` variable.
4. **Settings** → **Build and Deployment** → **Node.js Version**: **24.x** (as `web/.nvmrc`).
5. **Deploy**. *Expected:* `https://<project>.vercel.app` shows the sign-in page; a deep link such as
   `/requests/123` also loads the app (`web/vercel.json` rewrites every path to `index.html`).
6. **Allow the Vercel origin on the API.** Render → campusspace-api → **Environment** → `Cors__AllowedOrigins__0` =
   `https://<project>.vercel.app` (https, no path, no trailing slash) → **Save, rebuild, and deploy**.
   Preview deployments have other URLs, so the API refuses them (CORS); use the production URL.

## 6. First start and verification

**API log** (Render → campusspace-api → **Logs**), in this order:
```
Database: migrations on startup off; seed reference data only
Photo store: R2
No legacy photo folder; nothing to import into R2
Agent run poller: every 3 s while runs are live, else idle with a sweep every 30 min
Email: Brevo, redirect on            (or: disabled (no Email:BrevoApiKey; emails are Skipped), redirect off)
Notification dispatcher: every 5 s while emails are due, else idle with a sweep every 30 min
Now listening on: http://0.0.0.0:10000
Hosting environment: Production
```
Two DataProtection warnings ("Storing keys in a directory … not persisted", "No XML encryptor configured") are
expected and harmless: the API uses JWTs, not cookies. A pending migration stops startup with
"… database migration(s) are pending …": run step 2, then **Manual Deploy** → **Deploy latest commit**.
The seed logs nothing; `verify-deploy.sh` proves it (it signs in and lists the seeded rooms).

**Agent log**:
```
INFO: uvicorn.error: Started server process [1]
INFO: agent_service.main: checkpointer=postgres
INFO: uvicorn.error: Uvicorn running on http://0.0.0.0:10000
```
Render's `/health/live` pings are left out of both logs.

**Verify** (from the repo root; asks for the demo password with a hidden prompt):
```bash
./scripts/verify-deploy.sh --api https://campusspace-api.onrender.com \
  --agent https://campusspace-agent.onrender.com --web https://<project>.vercel.app
```
*Expected:* every line `PASS` and `All checks passed.` (liveness and `/health` on both services with
`checkpointer: postgres`, the database and `agent-service` Healthy, Swagger without `/internal` routes, CORS allowed for
the Vercel origin and refused for another, the React shell, a deep link, `VITE_API_URL` in the bundle, sign-in as
`perera@campusspace.local`, `/api/auth/me` and the seeded rooms). Paste the output to your reviewer. Then submit one
booking request in the web app (or the APK) and watch it reach "Pending approval": that exercises the agent → API
tool calls with the generated keys.

## 7. Free tiers, warm-up, and keeping it up until 21 Oct 2026

**Facts** (checked 2026-10-01):

- Render free web services: **750 free instance hours per month per workspace**, shared by both services; a service
  **spins down after 15 minutes without inbound traffic** and spins up on the next request, which "takes about one
  minute"; if the hours run out, Render **suspends all free web services until the next month**; Render "might restart
  a Free web service at any time"; free services can't receive private-network traffic (so the two services talk over
  their public HTTPS URLs) ([Render: Deploy for Free](https://render.com/docs/free)). Render does not document whether
  its own health checks count as traffic.
- Neon Free: **100 CU-hours per project per month**; compute **scales to zero after 5 minutes of inactivity, which the
  Free plan can't disable**; up to 2 CU; 0.5 GB storage ([Neon plans](https://neon.com/docs/introduction/plans)). A
  suspended compute wakes "in a few hundred milliseconds" ([Scale to zero](https://neon.com/docs/introduction/scale-to-zero)).
  When the CU-hours run out, the compute is suspended until the next monthly period: connections drop and new ones
  can't open ([Neon free plan FAQ](https://github.com/neondatabase/website/blob/main/content/faqs/free-plan-limits-and-quotas.md)).
  Neon doesn't say whether an idle open connection keeps the compute awake, so the numbers below assume it does.

**What this costs us.** The API's background loops (agent run poller, email dispatcher) used to query the database
every 3 s / 5 s whenever the API was awake, which would keep Neon awake too: at 0.25 CU, an API awake from 2 to
21 October (≈ 460 h) would use ≈ **115 CU-hours**, over the limit. Since this task they idle: with no live run and no
due email they make **no** query (measured: 0 transactions in 40 s, no open connection after 75 s) until the API itself
creates work (a submit, a decision, a new email wakes them at once) or a clock-aligned sweep every 30 minutes, which
both loops do together. Worst case now, even with the API awake all month: one Neon wake per 30 minutes ≈ 1 minute of
idle connection + 5 minutes before suspend ≈ 6 minutes of every 30 → 0.25 CU × 20 % × 460 h ≈ **23 CU-hours by
21 October** (≈ 37 for a whole month), plus about 0.25 CU-hour per hour of real use. Render: two services awake
24/7 would need 1,488 hours, twice the 750; the API alone around the clock (bots) would still fit (≈ 744).

**No keep-alive pings.** A 24/7 ping would spend ~1,488 Render hours and suspend both services before the deadline.
For the same reason the API does **not** ping the agent service while it is awake: with crawler traffic keeping the
API up around the clock, the agent would be kept up too and the two would exceed 750 hours. The agent service sleeps
whenever nothing calls it; the API waits for it to wake (below).

**Warm-up before a demo** (10 minutes before):
1. Open `https://campusspace-agent.onrender.com/health` (wakes the agent service and Neon; ~1 minute; you get the
   JSON with `"checkpointer_ok": true`).
2. Open `https://campusspace-api.onrender.com/health` (wakes the API; ~1 minute; `"status": "Healthy"` with
   `database` and `agent-service` Healthy).
3. Open the Vercel URL and sign in.
4. Optional: run `./scripts/verify-deploy.sh …` (step 6).
Both stay up for 15 minutes after their last request, and the demo itself keeps them up.

**Cold start during use.** If a request is submitted while the agent service sleeps, the request shows "Agent
processing" for one to two minutes: the API's inline start times out after 3 s and the poller retries the start every
3 s with the same run id until the agent answers (a start the waking agent already received counts as started). That
is not an error. Only if the agent stays unreachable for 4 minutes (`AgentService:StartTimeoutMinutes` in Production)
does the request become "Agent failed"; a Facilities Officer then uses **Retry agent**.

**Switching the agents to stubs** (saves Gemini credit; the workflow still works with deterministic workers): Render →
campusspace-agent → **Environment** → `AGENT_LLM_AGENTS` = empty → **Save, rebuild, and deploy**. Back to Gemini:
`supervisor,venue_matching,equipment_allocation,policy_cost`. `/health` shows each agent's mode.

**Check usage every 3–4 days until 21 Oct 2026:**
- Neon console → project → **Monitoring** / **Usage** (and **Billing**): compute (CU-hours) used this month.
- Render → **Workspace settings** → **Billing**: free instance hours used this month.
If Neon passes ~60 CU-hours or Render ~500 hours before 15 October, open the API's log for unusual traffic, switch the
agents to stubs, and tell your reviewer. Don't delete or recreate anything: the URLs must keep working until at least
21 October 2026 (plan §17.3), and a recreated Render service may get a different URL. Open every link in an incognito
window before submitting.

## 8. Rotating a secret (if one leaks)

- `AgentTools__Key` / `AgentService__ServiceKey`: Render → **Env Groups** → `campusspace-service-keys` → edit the value
  (paste `openssl rand -base64 32 | tr -d '\n' | pbcopy`; keep the two different) → save, then make sure BOTH services
  redeploy (if Render doesn't start them, **Manual Deploy** → **Deploy latest commit** on each): a service still on the
  old key fails every agent call until it restarts.
- `Jwt__Key`: campusspace-api → **Environment** → edit (same command) → save and deploy. Every user signs in again.
- A Neon role password: `./scripts/neon-db.sh password <role>` with a new value, then update `ConnectionStrings__Default`
  (API) or `AGENT_CHECKPOINT_URL` (agent) in Render and deploy.
- R2, Brevo, Gemini: create a new key in that dashboard, paste it in Render, deploy, then delete the old key there.
- `Seed__DemoPassword` can't be rotated this way: the seed never changes an existing password, and no endpoint changes
  a password. If it leaks, an Admin can deactivate the demo accounts (Users page) until you and your reviewer decide.

## 9. Rollback

- **Render:** the service → **Events** / **Deploys** → a previous successful deploy → **Rollback** → **Rollback to this
  deploy**. Its environment variables are rolled back with it (env group values are not). A dashboard rollback
  **turns auto-deploy off**: after the fix is merged, turn it back on in **Settings** → **Build & Deploy**
  ([Render: Rollbacks](https://render.com/docs/rollbacks)).
- **Vercel:** project overview → production deployment tile → **Instant Rollback** → choose the previous deployment →
  **Continue** → **Confirm Rollback**. The Hobby plan can roll back to the immediately previous production deployment.
  Afterwards new pushes don't go live until you click **Undo Rollback** (or promote a deployment)
  ([Vercel: Instant Rollback](https://vercel.com/docs/instant-rollback)).
- **Database:** migrations only go forward. Roll the API back only to a commit whose migrations are already applied
  (a newer schema with extra tables or columns is fine for an older API only if it added nothing NOT NULL that the old
  code doesn't fill); otherwise fix forward.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| API exits: "… migration(s) are pending …" | Run `./scripts/neon-db.sh migrate`, then redeploy. |
| API exits: "Cors origin … must use https" | `Cors__AllowedOrigins__0` must be `https://…vercel.app` with no path. |
| API exits about `SslMode` | The connection string needs `SslMode=VerifyFull` (or `Require`). If VerifyFull fails with a certificate error, use `SslMode=Require`. |
| API `/health`: `agent-service` Degraded | `AgentService__BaseUrl` is wrong, or the agent service is still waking (wait a minute). |
| Agent exits: "Checkpoint database unreachable (…)" | `AGENT_CHECKPOINT_URL`: pooled host, database `campusspace_agent`, the agent role's password. |
| Agent exits: "GOOGLE_API_KEY is required when AGENT_LLM_AGENTS enables an LLM agent …" | Set the key, or empty `AGENT_LLM_AGENTS` (stubs). |
| Every request "Agent failed: Agent service unreachable (last error: HTTP 401)" | The two services have different key values: both must use the env group. |
| Web: "Network Error" on sign-in | CORS (step 5.6) or `VITE_API_URL` (rebuild the Vercel deployment after changing it). |
| Everything down, Render says suspended | Free hours used up (step 7); they come back on the 1st. |
