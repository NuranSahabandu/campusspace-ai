import { Card, CardContent, Grid, Typography } from '@mui/material'
import { useAuthStore } from '../../auth/authStore'

// Placeholder KPI cards. Member 4 (D) replaces them with real data in Phase 1+ (§12).
const KPIS = ['Pending approvals', "Today's bookings", 'Utilization %', 'Agent success rate', 'Avg agent latency']

export function DashboardPage() {
  const user = useAuthStore((s) => s.user)

  return (
    <>
      <Typography variant="h4" component="h1" gutterBottom>
        Dashboard
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 3 }}>
        Welcome, {user?.fullName}.
      </Typography>
      <Grid container spacing={2}>
        {KPIS.map((label) => (
          <Grid key={label} size={{ xs: 12, sm: 6, lg: 4 }}>
            <Card variant="outlined">
              <CardContent>
                <Typography color="text.secondary" gutterBottom>
                  {label}
                </Typography>
                <Typography variant="h5" component="p">
                  —
                </Typography>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>
    </>
  )
}
