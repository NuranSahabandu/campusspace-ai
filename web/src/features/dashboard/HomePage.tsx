import { lazy, Suspense } from 'react'
import { LinearProgress, Typography } from '@mui/material'
import { useAuthStore } from '../../auth/authStore'
import { Roles } from '../../auth/roles'
import { AdminHome } from './AdminHome'

// The officer dashboard pulls in the reports code and, through it, the chart chunk; keep both out of the main bundle.
const OfficerDashboard = lazy(() => import('./OfficerDashboard').then((m) => ({ default: m.OfficerDashboard })))

/**
 * The landing page (/) for staff. Facilities Officers get the KPI dashboard (plan §12). Admins get links to their
 * pages: every dashboard figure comes from Officer-only endpoints (bookings, approvals, agent runs), which refuse Admins.
 */
export function HomePage() {
  const user = useAuthStore((s) => s.user)

  return (
    <>
      <Typography variant="h4" component="h1" gutterBottom>
        Dashboard
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 3 }}>
        Welcome, {user?.fullName}.
      </Typography>
      {user?.role === Roles.FacilitiesOfficer ? (
        <Suspense fallback={<LinearProgress />}>
          <OfficerDashboard />
        </Suspense>
      ) : (
        <AdminHome />
      )}
    </>
  )
}
