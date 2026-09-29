import type { AgentRunSummaryDto } from '../../api/types'
import { RequestStatuses } from '../requests/requestStatus'

/** AgentRunStatuses.Active on the server: a run that is still live. */
export const LIVE_RUN_STATUSES = ['Queued', 'Running', 'AwaitingApproval', 'Resuming'] as const

export const isLiveRun = (status: string) => (LIVE_RUN_STATUSES as readonly string[]).includes(status)

/**
 * The retry-agent button's label: "Retry agent" for an AgentFailed request, "Start agent" for a Submitted one with no
 * live run (seeded or legacy data), null otherwise. The server decides (409 "Only a failed or not-yet-started ...").
 */
export const retryAgentLabel = (status: string, runs: AgentRunSummaryDto[] | undefined): string | null => {
  if (status === RequestStatuses.AgentFailed) return 'Retry agent'
  if (status === RequestStatuses.Submitted && runs !== undefined && !runs.some((r) => isLiveRun(r.status)))
    return 'Start agent'
  return null
}
