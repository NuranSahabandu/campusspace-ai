import { lazy, type ReactNode, Suspense } from 'react'
import { LinearProgress } from '@mui/material'
import { Navigate, Route, Routes } from 'react-router'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { type AppPath, ROUTE_ROLES } from './auth/routeAccess'
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
const EquipmentTypesPage = lazy(() =>
  import('./features/equipment/EquipmentTypesPage').then((m) => ({ default: m.EquipmentTypesPage })),
)
const EquipmentItemsPage = lazy(() =>
  import('./features/equipment/EquipmentItemsPage').then((m) => ({ default: m.EquipmentItemsPage })),
)
const PricingPage = lazy(() => import('./features/pricing/PricingPage').then((m) => ({ default: m.PricingPage })))
const PolicyPage = lazy(() => import('./features/policy/PolicyPage').then((m) => ({ default: m.PolicyPage })))

/** A lazy page: the role guard for its path (ROUTE_ROLES) plus a Suspense boundary for the lazy chunk. */
function GuardedPage({ path, children }: { path: AppPath; children: ReactNode }) {
  return (
    <ProtectedRoute roles={ROUTE_ROLES[path]}>
      <Suspense fallback={<LinearProgress />}>{children}</Suspense>
    </ProtectedRoute>
  )
}

/** Route table. The router itself is supplied by main.tsx (browser) or the tests (memory). */
export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedRoute roles={ROUTE_ROLES['/']} />}>
        <Route element={<AppLayout />}>
          <Route index element={<DashboardPage />} />
          <Route path="rooms" element={<GuardedPage path="/rooms"><RoomsPage /></GuardedPage>} />
          <Route path="rooms/:id" element={<GuardedPage path="/rooms/:id"><RoomDetailPage /></GuardedPage>} />
          {/* Not a page of its own: the target's guard decides access. */}
          <Route path="equipment" element={<Navigate to="/equipment/types" replace />} />
          <Route path="equipment/types" element={<GuardedPage path="/equipment/types"><EquipmentTypesPage /></GuardedPage>} />
          <Route path="equipment/items" element={<GuardedPage path="/equipment/items"><EquipmentItemsPage /></GuardedPage>} />
          <Route path="pricing" element={<GuardedPage path="/pricing"><PricingPage /></GuardedPage>} />
          <Route path="policy" element={<GuardedPage path="/policy"><PolicyPage /></GuardedPage>} />
          <Route path="requests" element={<ComingSoonPage title="Requests" owner="C" />} />
          <Route path="approvals" element={<ComingSoonPage title="Approvals" owner="D" />} />
          <Route path="agent-runs" element={<ComingSoonPage title="Agent runs" owner="C/D" />} />
          <Route path="users" element={<GuardedPage path="/users"><UsersPage /></GuardedPage>} />
          <Route path="clubs" element={<GuardedPage path="/clubs"><ClubsPage /></GuardedPage>} />
          <Route path="clubs/:id" element={<GuardedPage path="/clubs/:id"><ClubDetailPage /></GuardedPage>} />
          <Route path="facilities/reference" element={<GuardedPage path="/facilities/reference"><ReferencePage /></GuardedPage>} />
          <Route path="audit-logs" element={<GuardedPage path="/audit-logs"><AuditLogsPage /></GuardedPage>} />
          <Route path="forbidden" element={<ForbiddenPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
