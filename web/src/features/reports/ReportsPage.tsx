import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { Box, Skeleton, Stack, TextField, Typography } from '@mui/material'
import type { ApprovalOutcomesDto } from '../../api/types'
import { campusAddDays, campusToday, formatDateOnly } from '../../ui/formatDateTime'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { formatRate } from '../agentRuns/format'
import { MetricCard } from '../agentRuns/MetricsCards'
import { BuildingUtilizationPanel } from './BuildingUtilizationPanel'
import { ChartPanel } from './ChartPanel'
import { formatUtilization, hourLabel, MAX_RANGE_DAYS, rangeDays, shortDate } from './format'
import { RoomUtilizationTable } from './RoomUtilizationTable'
import { type ReportRange, useDemandReport, useUtilizationReport } from './useReports'

const DATE = /^\d{4}-\d{2}-\d{2}$/
const parseDate = (raw: string | null) => (raw !== null && DATE.test(raw) ? raw : undefined)

/** The default range: the last 30 campus days, today included. */
export const DEFAULT_RANGE_DAYS = 30

const APPROVAL_HELP =
  'Requests the officer approved ÷ requests the officer approved or rejected, counted on the day of the decision. ' +
  'Closed automatically, cancelled before a decision, agent failures and revisions are not officer verdicts, so they ' +
  'are listed apart.'
const UTILIZATION_HELP =
  'Booked hours ÷ available hours of all active rooms (a ratio of sums). Available = the opening hours in the booking ' +
  'policy minus blackouts; booked = confirmed, checked-in and completed bookings inside those hours, minus blackouts.'

function rangeError(from: string, to: string): string | null {
  if (to < from) return 'To must be on or after From.'
  if (rangeDays(from, to) > MAX_RANGE_DAYS) return `A range can cover at most ${MAX_RANGE_DAYS} days.`
  return null
}


function Outcomes({ a }: { a: ApprovalOutcomesDto }) {
  const rows: [string, number][] = [
    ['Approved', a.approved],
    ['Rejected by an officer', a.officerRejected],
    ['Closed automatically (time no longer valid)', a.closedAutomatically],
    ['Cancelled before a decision', a.cancelledBeforeDecision],
    ['Agent failed', a.agentFailed],
    ['Revisions requested', a.revisionsRequested],
  ]
  return (
    <Box component="dl" sx={{ display: 'grid', gridTemplateColumns: 'auto auto', columnGap: 2, rowGap: 0.25, m: 0 }}>
      {rows.map(([label, n]) => (
        <Box key={label} sx={{ display: 'contents' }}>
          <Box component="dt">{label}</Box>
          <Box component="dd" sx={{ m: 0, textAlign: 'right' }}>
            {n}
          </Box>
        </Box>
      ))}
    </Box>
  )
}

/**
 * Utilization and demand reports (UC22, plan §12 "Pricing rules, reports"). The campus-date range lives in the URL. While
 * it is invalid nothing is sent and the last valid results stay on screen.
 */
export function ReportsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const today = campusToday()
  const from = parseDate(searchParams.get('from')) ?? campusAddDays(today, -(DEFAULT_RANGE_DAYS - 1))
  const to = parseDate(searchParams.get('to')) ?? today
  const error = rangeError(from, to)

  const key = `${from}|${to}`
  const [lastValid, setLastValid] = useState<(ReportRange & { key: string }) | null>(error ? null : { key, from, to })
  if (!error && lastValid?.key !== key) setLastValid({ key, from, to })
  const range: ReportRange | undefined = error ? (lastValid ?? undefined) : { from, to }
  const effective = range ?? { from, to }

  const utilization = useUtilizationReport({ from: effective.from, to: effective.to }, { enabled: range !== undefined })
  const demand = useDemandReport({ from: effective.from, to: effective.to }, { enabled: range !== undefined })

  const setParam = (name: 'from' | 'to', value: string) =>
    setSearchParams(
      (p) => {
        if (DATE.test(value)) p.set(name, value)
        else p.delete(name)
        return p
      },
      { replace: true },
    )

  const u = utilization.data
  const d = demand.data
  const busiestHour = d?.byHour.reduce((a, b) => (b.count > a.count ? b : a), d.byHour[0])

  return (
    <>
      <Typography variant="h4" component="h1" gutterBottom>
        Reports
      </Typography>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 1 }}>
        <TextField
          label="From"
          type="date"
          size="small"
          value={from}
          onChange={(e) => setParam('from', e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          label="To"
          type="date"
          size="small"
          value={to}
          onChange={(e) => setParam('to', e.target.value)}
          error={error !== null}
          helperText={error ?? ' '}
          slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: from } }}
        />
      </Stack>
      {range && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Showing {formatDateOnly(range.from)} – {formatDateOnly(range.to)} (campus dates, inclusive).
        </Typography>
      )}

      <Typography variant="h5" component="h2" sx={{ mt: 2, mb: 1 }}>
        Utilization
      </Typography>
      {utilization.isError ? (
        <QueryErrorAlert error={utilization.error} what="the utilization report" onRetry={() => utilization.refetch()} />
      ) : !u ? (
        <Skeleton variant="rectangular" height={300} aria-label="Loading the utilization report" />
      ) : (
        <Stack spacing={2}>
          <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 2fr' }, alignItems: 'start' }}>
            <MetricCard title="Overall utilization" help={UTILIZATION_HELP} value={formatUtilization(u.overall)}>
              Active rooms only
            </MetricCard>
            <BuildingUtilizationPanel buildings={u.buildings} overall={u.overall} />
          </Box>
          <RoomUtilizationTable rooms={u.rooms} />
        </Stack>
      )}

      <Typography variant="h5" component="h2" sx={{ mt: 4, mb: 1 }}>
        Demand
      </Typography>
      {demand.isError ? (
        <QueryErrorAlert error={demand.error} what="the demand report" onRetry={() => demand.refetch()} />
      ) : !d ? (
        <Skeleton variant="rectangular" height={300} aria-label="Loading the demand report" />
      ) : (
        <Stack spacing={2}>
          <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, alignItems: 'start' }}>
            <MetricCard
              title="Requests submitted"
              help="Booking requests submitted in the range (by the day they were submitted)."
              value={String(d.total)}
            >
              {rangeDays(d.from, d.to)} days
            </MetricCard>
            <MetricCard
              title="Approval rate"
              help={APPROVAL_HELP}
              value={formatRate(d.approvals.approvalRate, d.approvals.approved, d.approvals.decided)}
            >
              <Outcomes a={d.approvals} />
            </MetricCard>
          </Box>
          <ChartPanel
            title="Requests per day"
            summary={`${d.total} ${d.total === 1 ? 'request' : 'requests'} submitted between ${formatDateOnly(d.from)} and ${formatDateOnly(d.to)}.`}
            data={d.byDay.map((x) => ({ label: shortDate(x.date), value: x.count }))}
            valueLabel="Requests"
            formatValue={(v) => String(v)}
            rows={d.byDay}
            rowKey={(x) => x.date}
            columns={[
              { header: 'Date', cell: (x) => formatDateOnly(x.date) },
              { header: 'Requests', cell: (x) => x.count, numeric: true },
            ]}
            emptyText={d.total === 0 ? 'No requests were submitted in this range.' : undefined}
          />
          <ChartPanel
            title="Requests by requested start hour"
            summary={
              d.total === 0 || !busiestHour
                ? 'No requests were submitted in this range.'
                : `The most requested start hour is ${hourLabel(busiestHour.hour)} (${busiestHour.count} of ${d.total}), campus time.`
            }
            data={d.byHour.map((x) => ({ label: hourLabel(x.hour), value: x.count }))}
            valueLabel="Requests"
            formatValue={(v) => String(v)}
            rows={d.byHour}
            rowKey={(x) => x.hour}
            columns={[
              { header: 'Start hour', cell: (x) => hourLabel(x.hour) },
              { header: 'Requests', cell: (x) => x.count, numeric: true },
            ]}
            emptyText={d.total === 0 ? 'No requests were submitted in this range.' : undefined}
          />
        </Stack>
      )}

      <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>
        Available hours use the current booking policy's opening hours and the rooms' current active status for the whole
        range; their history is not stored, so a past range is measured against today's settings.
      </Typography>
    </>
  )
}
