import type { DashboardDto, DemandReportDto } from '../../api/types'

// Hand-written (not captures): every denominator is 0 and every rate null, the case the captured data can't show.

export const EMPTY_DASHBOARD: DashboardDto = {
  today: '2026-10-01',
  from: '2026-09-25',
  to: '2026-10-01',
  pendingApprovals: 0,
  todayBookings: 0,
  utilization: { bookedHours: 0, availableHours: 0, utilization: null },
  utilizationByBuilding: [],
  bookingsPerDay: ['2026-09-25', '2026-09-26', '2026-09-27', '2026-09-28', '2026-09-29', '2026-09-30', '2026-10-01'].map(
    (date) => ({ date, count: 0 }),
  ),
  agent: { successRate: null, reachedGate: 0, finished: 0, avgProcessingMs: null, processingRuns: 0 },
}

export const EMPTY_DEMAND: DemandReportDto = {
  from: '2026-09-20',
  to: '2026-09-20',
  total: 0,
  byDay: [{ date: '2026-09-20', count: 0 }],
  byHour: Array.from({ length: 24 }, (_, hour) => ({ hour, count: 0 })),
  approvals: {
    approved: 0,
    officerRejected: 0,
    decided: 0,
    approvalRate: null,
    closedAutomatically: 0,
    cancelledBeforeDecision: 0,
    agentFailed: 0,
    revisionsRequested: 0,
  },
}
