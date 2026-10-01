import { Link as RouterLink } from 'react-router'
import { Box, Link, Skeleton, Stack } from '@mui/material'
import { formatDateOnly } from '../../ui/formatDateTime'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { formatMs, formatRate } from '../agentRuns/format'
import { MetricCard } from '../agentRuns/MetricsCards'
import { BuildingUtilizationPanel } from '../reports/BuildingUtilizationPanel'
import { ChartPanel } from '../reports/ChartPanel'
import { formatUtilization, shortDate } from '../reports/format'
import { useDashboard } from '../reports/useReports'

const UTILIZATION_HELP =
  'Booked hours ÷ available hours of all active rooms. Available = the opening hours in the booking policy minus ' +
  'blackouts; booked = confirmed, checked-in and completed bookings inside those hours.'
const SUCCESS_HELP =
  'Agent runs that reached the approval gate ÷ finished runs, as on the Agent runs page (runs still working are left out).'
const PROCESSING_HELP =
  'Average agent processing time of the runs that reached the approval gate: the sum of their step durations, as on ' +
  'the Agent runs page.'

const processing = (avg: number | null, runs: number) =>
  runs === 0 ? '— (0 runs)' : `${formatMs(avg)} (${runs} ${runs === 1 ? 'run' : 'runs'})`

/** The Facilities Officer's KPI cards and charts (plan §12 Dashboard). Figures are for the last 7 campus days. */
export function OfficerDashboard() {
  const dashboard = useDashboard()

  if (dashboard.isPending)
    return (
      <Stack spacing={2} aria-label="Loading the dashboard">
        <Skeleton variant="rectangular" height={110} />
        <Skeleton variant="rectangular" height={300} />
      </Stack>
    )
  if (dashboard.isError)
    return <QueryErrorAlert error={dashboard.error} what="the dashboard" onRetry={() => dashboard.refetch()} />

  const d = dashboard.data
  const range = `${formatDateOnly(d.from)} – ${formatDateOnly(d.to)}`
  const bookings = d.bookingsPerDay.reduce((sum, day) => sum + day.count, 0)
  const busiest = d.bookingsPerDay.reduce((a, b) => (b.count > a.count ? b : a), d.bookingsPerDay[0])

  return (
    <Stack spacing={2}>
      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', lg: 'repeat(5, 1fr)' } }}>
        <MetricCard title="Pending approvals" help="Requests waiting for an officer's decision now." value={String(d.pendingApprovals)}>
          <Link component={RouterLink} to="/approvals">
            Open the approval queue
          </Link>
        </MetricCard>
        <MetricCard
          title="Today's bookings"
          help="Confirmed, checked-in and completed bookings that overlap today (campus time)."
          value={String(d.todayBookings)}
        >
          {formatDateOnly(d.today)}
        </MetricCard>
        <MetricCard title="Utilization" help={UTILIZATION_HELP} value={formatUtilization(d.utilization)}>
          Last 7 days · <Link component={RouterLink} to="/reports">Reports</Link>
        </MetricCard>
        <MetricCard
          title="Agent success rate"
          help={SUCCESS_HELP}
          value={formatRate(d.agent.successRate, d.agent.reachedGate, d.agent.finished)}
        >
          Last 7 days · <Link component={RouterLink} to="/agent-runs">Agent runs</Link>
        </MetricCard>
        <MetricCard
          title="Avg agent processing time"
          help={PROCESSING_HELP}
          value={processing(d.agent.avgProcessingMs, d.agent.processingRuns)}
        >
          Runs that reached the approval gate
        </MetricCard>
      </Box>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
        <ChartPanel
          title="Bookings per day"
          summary={
            bookings === 0
              ? `No bookings start in ${range}.`
              : `${bookings} ${bookings === 1 ? 'booking starts' : 'bookings start'} in ${range}; the busiest day is ${formatDateOnly(busiest.date)} (${busiest.count}).`
          }
          data={d.bookingsPerDay.map((day) => ({ label: shortDate(day.date), value: day.count }))}
          valueLabel="Bookings"
          formatValue={(v) => String(v)}
          rows={d.bookingsPerDay}
          rowKey={(day) => day.date}
          columns={[
            { header: 'Date', cell: (day) => formatDateOnly(day.date) },
            { header: 'Bookings', cell: (day) => day.count, numeric: true },
          ]}
          emptyText={bookings === 0 ? 'No bookings in the last 7 days.' : undefined}
        />
        <BuildingUtilizationPanel
          title="Utilization by building"
          buildings={d.utilizationByBuilding}
          overall={d.utilization}
        />
      </Box>
    </Stack>
  )
}
