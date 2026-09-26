import { lazy, type ReactNode, Suspense } from 'react'
import { LinearProgress } from '@mui/material'
import { Route, Routes } from 'react-router'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { Roles, STAFF_ROLES } from './auth/roles'
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

/** Admin-only page: role guard plus a Suspense boundary for the lazy chunk. */
function AdminPage({ children }: { children: ReactNode }) {
  return (
    <ProtectedRoute roles={[Roles.Admin]}>
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
          <Route path="rooms" element={<ComingSoonPage title="Rooms" owner="A" />} />
          <Route path="equipment" element={<ComingSoonPage title="Equipment" owner="B" />} />
          <Route path="requests" element={<ComingSoonPage title="Requests" owner="C" />} />
          <Route path="approvals" element={<ComingSoonPage title="Approvals" owner="D" />} />
          <Route path="agent-runs" element={<ComingSoonPage title="Agent runs" owner="C/D" />} />
          <Route path="users" element={<AdminPage><UsersPage /></AdminPage>} />
          <Route path="clubs" element={<AdminPage><ClubsPage /></AdminPage>} />
          <Route path="clubs/:id" element={<AdminPage><ClubDetailPage /></AdminPage>} />
          <Route path="audit-logs" element={<AdminPage><AuditLogsPage /></AdminPage>} />
          <Route path="forbidden" element={<ForbiddenPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
