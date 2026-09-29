import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { type ListParams, usePagedQuery } from '../../api/list'
import { parseProblem } from '../../api/problem'
import type { AgentRunDetailDto, AgentRunSummaryDto, ApprovalQueueItemDto, QuotationDto } from '../../api/types'
import { bookingRequestsKeys } from '../requests/useRequests'

/** The approval queue re-fetches every 15 s while it is open (plan §12). */
export const QUEUE_REFRESH_MS = 15_000
/** While an approval is being finished (approve answered 202), the request is re-fetched every 3 s. */
export const APPROVAL_IN_PROGRESS_REFRESH_MS = 3_000

/**
 * Query keys. Runs, run details and quotations sit under bookingRequestsKeys.all, so every write that invalidates the
 * requests (cancel, retry-agent, decisions) refreshes them too. `version` is the request's updatedAt: when the request
 * changes (a new proposal, an approval), the dependent queries load again.
 */
export const approvalsKeys = {
  all: ['approvals'] as const,
  queue: (params: ListParams) => [...approvalsKeys.all, 'queue', params] as const,
  runs: (requestId: number, version: string) => [...bookingRequestsKeys.all, 'agent-runs', requestId, version] as const,
  run: (runId: string, version: string) => [...bookingRequestsKeys.all, 'agent-run', runId, version] as const,
  quotation: (requestId: number, version: string) => [...bookingRequestsKeys.all, 'quotation', requestId, version] as const,
}

/** Grid column field → the server sort field (ApprovalQueueQuery.SortFields). */
export const APPROVAL_QUEUE_SORT_FIELDS: Record<string, string> = {
  pendingSince: 'pendingSince',
  when: 'requestedStart',
  attendees: 'attendees',
}

export function useApprovalQueue(params: ListParams) {
  return usePagedQuery<ApprovalQueueItemDto>(approvalsKeys.queue(params), '/api/approvals/queue', params, {
    refetchInterval: () => QUEUE_REFRESH_MS,
  })
}

/** The request's agent runs, newest first (Facilities Officer). */
export function useRequestRuns(requestId: number, version: string | undefined) {
  return useQuery({
    queryKey: approvalsKeys.runs(requestId, version ?? ''),
    queryFn: async ({ signal }) =>
      (await api.get<AgentRunSummaryDto[]>(`/api/booking-requests/${requestId}/agent-runs`, { signal })).data,
    enabled: version !== undefined,
    placeholderData: keepPreviousData,
  })
}

/** One run with its proposal, trace, checklist and decisions. */
export function useAgentRun(runId: string | undefined, version: string | undefined) {
  return useQuery({
    queryKey: approvalsKeys.run(runId ?? '', version ?? ''),
    queryFn: async ({ signal }) => (await api.get<AgentRunDetailDto>(`/api/agent-runs/${runId}`, { signal })).data,
    enabled: runId !== undefined && version !== undefined,
    placeholderData: keepPreviousData,
  })
}

/** The request's live (Draft or Issued) quotation from .NET, or null when it has none (404). */
export function useRequestQuotation(requestId: number, version: string | undefined) {
  return useQuery({
    queryKey: approvalsKeys.quotation(requestId, version ?? ''),
    queryFn: async ({ signal }) => {
      try {
        return (await api.get<QuotationDto>(`/api/booking-requests/${requestId}/quotation`, { signal })).data
      } catch (error) {
        if (parseProblem(error).status === 404) return null
        throw error
      }
    },
    enabled: version !== undefined,
    placeholderData: keepPreviousData,
  })
}
