import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { MAIN_QUERY_META } from '../../api/forbidden'
import type { DashboardDto, DemandReportDto, UtilizationReportDto } from '../../api/types'
import { bookingRequestsKeys } from '../requests/useRequests'

/** A campus-date range, inclusive (yyyy-MM-dd). */
export interface ReportRange {
  from: string
  to: string
}

/**
 * Query keys. They sit under bookingRequestsKeys.all, so every write that invalidates the requests (decisions, cancel,
 * retry-agent) refreshes the reports and the dashboard too.
 */
export const reportsKeys = {
  all: [...bookingRequestsKeys.all, 'reports'] as const,
  utilization: (range: ReportRange) => [...reportsKeys.all, 'utilization', range] as const,
  demand: (range: ReportRange) => [...reportsKeys.all, 'demand', range] as const,
  dashboard: () => [...reportsKeys.all, 'dashboard'] as const,
}

export function useUtilizationReport(range: ReportRange, options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: reportsKeys.utilization(range),
    queryFn: async ({ signal }) =>
      (await api.get<UtilizationReportDto>('/api/reports/utilization', { params: range, signal })).data,
    placeholderData: keepPreviousData,
    enabled: options.enabled,
    meta: MAIN_QUERY_META,
  })
}

export function useDemandReport(range: ReportRange, options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: reportsKeys.demand(range),
    queryFn: async ({ signal }) => (await api.get<DemandReportDto>('/api/reports/demand', { params: range, signal })).data,
    placeholderData: keepPreviousData,
    enabled: options.enabled,
    meta: MAIN_QUERY_META,
  })
}

export function useDashboard() {
  return useQuery({
    queryKey: reportsKeys.dashboard(),
    queryFn: async ({ signal }) => (await api.get<DashboardDto>('/api/reports/dashboard', { signal })).data,
    meta: MAIN_QUERY_META,
  })
}
