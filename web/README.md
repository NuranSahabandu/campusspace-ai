# web

React 19 + TypeScript + Vite staff portal (Facilities Officers and Admins). Shell owned by Member 4 (D); each
component adds its own pages under `src/features/`.

```bash
npm ci
npm run dev            # http://localhost:5173 (strict port); needs the API on :5080
npm run lint           # oxlint
npm test -- --run      # Vitest + Testing Library + MSW, no real network
npm run build          # tsc -b && vite build
```

`VITE_API_URL` comes from the repo-root `.env` (`envDir: '..'`). Only `VITE_*` variables reach the browser,
and anything in the bundle is public, so never prefix a secret with `VITE_`.

## Structure (`src/`)

| Path | What it is |
|------|------------|
| `api/client.ts` | The axios instance. Adds the Bearer token; a 401 (except on login) logs out and redirects to `/login?expired=1` |
| `api/problem.ts` | `parseProblem` (RFC 9457 → `{ title, status, traceId, fieldErrors }`) and `applyFieldErrors` for React Hook Form |
| `auth/` | `roles.ts` (mirrors `Models/Roles.cs`), `authStore.ts` (Zustand, persisted), `ProtectedRoute.tsx` |
| `layout/` | `AppLayout` (AppBar, role-filtered drawer, user menu) and `navItems.tsx` |
| `features/users/` | **Reference data view.** Copy it for new list pages: server-mode DataGrid + TanStack Query, search/filter/sort/paging mapped to `?search=&sort=&page=&pageSize=`, loading/empty/error states |
| `ui/toastStore.ts` | `toast.success(...)` / `toast.error(...)` |
| `test/` | Vitest setup, the MSW server, fixtures and `renderApp(route, { role })` |

State (ADR-1): TanStack Query for server data, Zustand for the session and small UI state.
