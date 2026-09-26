import { lazy, type ReactNode, Suspense } from 'react'
import { LinearProgress } from '@mui/material'
import { Route, Routes } from 'react-router'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { type Role, Roles, STAFF_ROLES } from './auth/roles'
import { LoginPage } from './features/auth/LoginPage'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { ForbiddenPage } from './features/errors/ForbiddenPage'
import { NotFoundPage } from './features/errors/NotFoundPage'
import { ComingSoonPage } from './features/placeholder/ComingSoonPage'
import { AppLayout } from './layout/AppLayout'

// The DataGrid is most of the bundle; load it only when a data page is opened.
const UsersPage = lazy(() => import('./features/users/UsersPage').then((m) => ({ default: m.UsersPage })))
const ClubsPage = lazy(() => import('./features/clubs/ClubsPage').then((m) => ({ default: m.ClubsPage })))
const AuditLogsPage = lazy(() => import('./features/audit/AuditLogsPage').then((m) => ({ default: m.AuditLogsPage })))
const ClubDetailPage = lazy(() => import('./features/clubs/ClubDetailPage').then((m) => ({ default: m.ClubDetailPage })))
const RoomsPage = lazy(() => import('./features/facilities/RoomsPage').then((m) => ({ default: m.RoomsPage })))
const RoomDetailPage = lazy(() =>
  import('./features/facilities/RoomDetailPage').then((m) => ({ default: m.RoomDetailPage })),
)
const ReferencePage = lazy(() =>
  import('./features/facilities/ReferencePage').then((m) => ({ default: m.ReferencePage })),
)

const ADMIN: readonly Role[] = [Roles.Admin]
// The facilities write endpoints are Officer-only, so Admins do not get these screens.
const OFFICER: readonly Role[] = [Roles.FacilitiesOfficer]

/** A lazy page for some roles: role guard plus a Suspense boundary for the lazy chunk. */
function GuardedPage({ roles, children }: { roles: readonly Role[]; children: ReactNode }) {
  return (
    <ProtectedRoute roles={roles}>
      <Suspense fallback={<LinearProgress />}>{children}</Suspense>
    </ProtectedRoute>
  )
}

/** Route table. The router itself is supplied by main.tsx (browser) or the tests (memory). */
export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedRoute roles={STAFF_ROLES} />}>
        <Route element={<AppLayout />}>
          <Route index element={<DashboardPage />} />
          <Route path="rooms" element={<GuardedPage roles={OFFICER}><RoomsPage /></GuardedPage>} />
          <Route path="rooms/:id" element={<GuardedPage roles={OFFICER}><RoomDetailPage /></GuardedPage>} />
          <Route path="equipment" element={<ComingSoonPage title="Equipment" owner="B" />} />
          <Route path="requests" element={<ComingSoonPage title="Requests" owner="C" />} />
          <Route path="approvals" element={<ComingSoonPage title="Approvals" owner="D" />} />
          <Route path="agent-runs" element={<ComingSoonPage title="Agent runs" owner="C/D" />} />
          <Route path="users" element={<GuardedPage roles={ADMIN}><UsersPage /></GuardedPage>} />
          <Route path="clubs" element={<GuardedPage roles={ADMIN}><ClubsPage /></GuardedPage>} />
          <Route path="clubs/:id" element={<GuardedPage roles={ADMIN}><ClubDetailPage /></GuardedPage>} />
          <Route path="facilities/reference" element={<GuardedPage roles={OFFICER}><ReferencePage /></GuardedPage>} />
          <Route path="audit-logs" element={<GuardedPage roles={ADMIN}><AuditLogsPage /></GuardedPage>} />
          <Route path="forbidden" element={<ForbiddenPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
