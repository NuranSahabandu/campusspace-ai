import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { compactParams, type ListParams, usePagedQuery } from '../../api/list'
import type { AgentRunDetailDto, AgentRunListItemDto, AgentRunMetricsDto } from '../../api/types'
import { isLiveRun } from '../approvals/agentRuns'
import { bookingRequestsKeys } from '../requests/useRequests'

/** While a run is live (Queued, Running, AwaitingApproval, Resuming) its detail re-fetches every 3 s. */
export const LIVE_RUN_REFRESH_MS = 3_000

/** The runs list's filters (GET /api/agent-runs). from/to are campus dates, inclusive, on the run's CreatedAt. */
export interface AgentRunsParams extends ListParams {
  status?: string[]
  from?: string
  to?: string
  requestId?: number
  fallback?: boolean
}

export interface MetricsRange {
  from?: string
  to?: string
}

/**
 * Query keys. They sit under bookingRequestsKeys.all, so every write that invalidates the requests (decisions,
 * cancel, retry-agent) refreshes the monitor too.
 */
export const agentRunsKeys = {
  all: [...bookingRequestsKeys.all, 'agent-runs-monitor'] as const,
  list: (params: AgentRunsParams) => [...agentRunsKeys.all, 'list', params] as const,
  metrics: (range: MetricsRange) => [...agentRunsKeys.all, 'metrics', range] as const,
  detail: (runId: string) => [...agentRunsKeys.all, 'detail', runId] as const,
}

/** Grid column field → the server sort field (AgentRunsQuery.SortFields). */
export const AGENT_RUNS_SORT_FIELDS: Record<string, string> = {
  createdAt: 'createdAt',
  durationMs: 'durationMs',
  status: 'status',
}

export function useAgentRunsList(params: AgentRunsParams, options: { enabled?: boolean } = {}) {
  return usePagedQuery<AgentRunListItemDto>(agentRunsKeys.list(params), '/api/agent-runs', params, options)
}

export function useAgentRunMetrics(range: MetricsRange, options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: agentRunsKeys.metrics(range),
    queryFn: async ({ signal }) =>
      (await api.get<AgentRunMetricsDto>('/api/agent-runs/metrics', { params: compactParams(range), signal })).data,
    placeholderData: keepPreviousData,
    enabled: options.enabled,
  })
}

/** One run with its trace, checklist, decisions and policy snapshot; re-fetched while the run is live. */
export function useAgentRunDetail(runId: string | undefined) {
  return useQuery({
    queryKey: agentRunsKeys.detail(runId ?? ''),
    queryFn: async ({ signal }) => (await api.get<AgentRunDetailDto>(`/api/agent-runs/${runId}`, { signal })).data,
    enabled: runId !== undefined,
    refetchInterval: (query) => (query.state.data && isLiveRun(query.state.data.status) ? LIVE_RUN_REFRESH_MS : false),
  })
}
