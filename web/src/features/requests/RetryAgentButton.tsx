import ReplayIcon from '@mui/icons-material/Replay'
import { Button } from '@mui/material'
import { api } from '../../api/client'
import type { AgentRunSummaryDto, BookingRequestDetailDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { retryAgentLabel } from '../approvals/agentRuns'
import { bookingRequestsKeys } from './useRequests'

/**
 * POST /api/booking-requests/{id}/retry-agent (Facilities Officer): 202 starts a new run (the request moves to
 * AgentProcessing and the page follows it); a 409 (not restartable, or the requester is at the open-request cap) is
 * shown as a toast with the server's message exactly as sent.
 */
export function RetryAgentButton({
  request,
  runs,
}: {
  request: BookingRequestDetailDto
  runs: AgentRunSummaryDto[] | undefined
}) {
  const mutation = useApiMutation<void, BookingRequestDetailDto>({
    mutationFn: async () => (await api.post<BookingRequestDetailDto>(`/api/booking-requests/${request.id}/retry-agent`)).data,
    invalidate: [bookingRequestsKeys.all],
    successMessage: 'Agent started: a new proposal is being prepared',
  })
  const label = retryAgentLabel(request.status, runs)
  if (!label) return null
  return (
    <Button variant="outlined" startIcon={<ReplayIcon />} onClick={() => mutation.mutate()} disabled={mutation.isPending}>
      {label}
    </Button>
  )
}
