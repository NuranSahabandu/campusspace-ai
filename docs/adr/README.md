# Architecture Decision Records

One file per decision, named `NNNN-short-title.md`.

## Notes

- React 19 instead of plan's 18: Vite default; all chosen libraries support it.
- Charts: `@mui/x-charts` 9.14 (MIT) instead of the plan's Recharts (§6). Measured with one lazy bar chart in our
  build: x-charts chunk 276.4 kB / 86.4 kB gzip vs recharts 3.10 346.2 kB / 100.3 kB gzip; the main chunk is unchanged
  either way (lazy only). x-charts shares @mui/material and @mui/x-internals with the DataGrid we already ship and
  follows the MUI theme. Only `web/src/features/reports/charts/` imports it.
- Damage photos (Task 6.D1): a **private Cloudflare R2 bucket, read only through the API** (`GET /api/loans/{id}/photo`
  streams it), chosen over:
  - `bytea` in PostgreSQL: simplest and transactional, but photos (up to 5 MB) would fill Neon's free 0.5 GB and its
    backups, and every view would load the database.
  - Cloudinary: a free image CDN, but delivery URLs are public by default and its transformations aren't needed; damage
    evidence is not public media.
  - R2 with signed URLs (redirect): saves API bandwidth, but a signed URL is a bearer link that anyone holding it can
    open until it expires (logs, history, forwarding), bypasses our role check, can't be revoked and needs bucket CORS.
  - Render's disk: wiped on every deploy, so Production refuses it.
  Consequences: the API proxies every photo byte (acceptable: ≤ 5 MB, viewed rarely); one more secret set (`R2__*`);
  the upload can't share the database transaction, so check-in uploads first, outside any lock, and deletes the object
  if the commit fails (a failed delete leaves a logged, private, unreferenced orphan). R2's free tier has 10 GB and no
  egress fees, and its S3 API keeps the code portable (AWSSDK.S3).
- Agent checkpoints (Task 6.D2): **PostgresSaver (langgraph-checkpoint-postgres 3.x, psycopg 3 pool)**. This is a
  narrow, deliberate deviation from plan §7.1 rule 3 ("the agent service has no database credentials"), which §10.9
  itself anticipates (a Postgres checkpointer "in a separate schema" on an ephemeral host).
  - **Why.** SqliteSaver on Render's disk would lose every approval paused at the human gate on the next deploy or
    restart.
  - **Least privilege.** The agent service gets credentials ONLY for its own checkpoint storage: the role
    `campusspace_agent`, which owns the database `campusspace_agent` (schema `agent_checkpoints`). CONNECT on the
    business database is revoked from PUBLIC, so the role can't read or change any business table. Bookings,
    reservations and emails still happen only in .NET after officer approval, and the agent still reads business data
    only through `/internal/agent-tools`.
  - **Considered.**
    - SQLite on a persistent disk: Render's free plan has none.
    - A schema inside the business database with the app role: it would hand the agent business credentials.
  - **Consequences.**
    - One more secret: `AGENT_CHECKPOINT_URL`.
    - The service refuses to start without it outside `AGENT_ENV=development`.
    - Switching stores loses in-flight threads, which .NET handles through its not-found and approval-failure paths.
