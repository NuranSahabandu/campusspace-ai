import { useState } from 'react'
import { Link as RouterLink, useLocation, useParams } from 'react-router'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import CancelIcon from '@mui/icons-material/Cancel'
import FactCheckIcon from '@mui/icons-material/FactCheck'
import { Alert, Box, Button, Chip, LinearProgress, Stack, Typography } from '@mui/material'
import { parseProblem } from '../../api/problem'
import type { BookingRequestDetailDto, LatestProposalDto } from '../../api/types'
import { formatCampusTimeRange, formatDateTime } from '../../ui/formatDateTime'
import { formatLkr } from '../../ui/formatLkr'
import { RunHistory } from '../approvals/RunHistory'
import { EmailStatusCard } from '../notifications/EmailStatusCard'
import { useRequestRuns } from '../approvals/useApprovals'
import type { FromListState } from './BookingRequestsPage'
import { CancelRequestDialog } from './CancelRequestDialog'
import { Card, Fact, RequestCards, Timeline } from './RequestCards'
import { RequestStatusChip } from './RequestStatusChip'
import { RetryAgentButton } from './RetryAgentButton'
import { isCancellable, RequestStatuses } from './requestStatus'
import { useBookingRequest } from './useRequests'

const parseId = (raw: string | undefined) => (raw !== undefined && /^[1-9]\d*$/.test(raw) ? Number(raw) : undefined)

/**
 * One booking request for a Facilities Officer (§12, Component C). The officer can cancel it (with a reason) while its
 * status allows, open the approval screen while it waits for a decision, and (re)start the agents for a failed or
 * not-yet-started request.
 */
export function BookingRequestDetailPage() {
  const id = parseId(useParams().id)
  const location = useLocation()
  // Back to the list with the filters it had, when we came from it.
  const listSearch = (location.state as Partial<FromListState> | null)?.listSearch ?? ''
  const { data: request, isPending, isError, error, refetch } = useBookingRequest(id ?? 0)
  const runs = useRequestRuns(id ?? 0, request?.updatedAt)
  const [cancelling, setCancelling] = useState(false)

  const back = (
    <Button component={RouterLink} to={`/requests${listSearch}`} startIcon={<ArrowBackIcon />} sx={{ mb: 1 }}>
      Back to requests
    </Button>
  )

  const notFound = (
    <>
      {back}
      <Alert severity="warning">Request not found. It may not exist, or you may not have access to it.</Alert>
    </>
  )

  if (id === undefined) return notFound
  if (isPending) return <LinearProgress aria-label="Loading request" />
  if (isError) {
    const problem = parseProblem(error)
    if (problem.status === 404 || problem.status === 403) return notFound
    return (
      <>
        {back}
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => refetch()}>
              Retry
            </Button>
          }
        >
          Could not load the request: {problem.title}
        </Alert>
      </>
    )
  }

  return (
    <>
      {back}
      <Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 2 }}>
        <Typography variant="h4" component="h1">
          {request.purpose}
        </Typography>
        <RequestStatusChip status={request.status} size="medium" />
        <Stack direction="row" spacing={1} useFlexGap sx={{ ml: 'auto', flexWrap: 'wrap' }}>
          {request.status === RequestStatuses.PendingApproval && (
            <Button
              variant="contained"
              startIcon={<FactCheckIcon />}
              component={RouterLink}
              to={`/approvals/${request.id}`}
            >
              Review proposal
            </Button>
          )}
          <RetryAgentButton request={request} runs={runs.data} />
          {isCancellable(request.status) && (
            <Button color="error" variant="outlined" startIcon={<CancelIcon />} onClick={() => setCancelling(true)}>
              Cancel request
            </Button>
          )}
        </Stack>
      </Stack>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
        {request.cancelledAt && <CancellationCard request={request} cancelledAt={request.cancelledAt} />}
        <RequestCards request={request} />
        <Card title="Status history" wide>
          <Timeline history={request.history} />
        </Card>
        <Card title="Agent proposal" wide>
          {request.latestProposal ? (
            <LatestProposalSummary proposal={request.latestProposal} />
          ) : (
            <Typography color="text.secondary">
              No proposal yet. The proposal, validation checklist and agent trace appear once the agents have planned.
            </Typography>
          )}
        </Card>
        <Card title="Agent runs" wide>
          <RunHistory query={runs} />
        </Card>
        <EmailStatusCard requestId={request.id} version={request.updatedAt} />
      </Box>

      {cancelling && (
        <CancelRequestDialog
          requestId={request.id}
          message={`Cancel "${request.purpose}" by ${request.requester.name} (${formatCampusTimeRange(request.requestedStart, request.requestedEnd)})? An approved booking is released.`}
          onClose={() => setCancelling(false)}
        />
      )}
    </>
  )
}

/** The live proposal's summary (the same as the requester sees): room, .NET quote total and revision. */
function LatestProposalSummary({ proposal }: { proposal: LatestProposalDto }) {
  return (
    <Box sx={{ display: 'grid', gap: 1, gridTemplateColumns: { xs: '1fr', sm: 'repeat(3, 1fr)' } }}>
      <Fact label="Room">{proposal.roomCode ? `${proposal.roomCode} · ${proposal.roomName}` : 'Unknown room'}</Fact>
      <Fact label={proposal.quoteStatus === 'Issued' ? 'Issued quote' : 'Draft quote'}>
        {proposal.exempt ? 'Fee-exempt' : formatLkr(proposal.total)}
      </Fact>
      <Fact label="Revision">{proposal.revisionNo}</Fact>
    </Box>
  )
}

/** How and when the request was cancelled. The reason is the Cancelled history row's, shown as plain text. */
function CancellationCard({ request, cancelledAt }: { request: BookingRequestDetailDto; cancelledAt: string }) {
  const reason = request.history.findLast((h) => h.toStatus === RequestStatuses.Cancelled)?.reason
  return (
    <Card title="Cancellation" wide>
      <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography>Cancelled on {formatDateTime(cancelledAt)}</Typography>
        {request.isLateCancellation && <Chip size="small" color="warning" label="Late cancellation" />}
      </Stack>
      {request.cancelledByOfficer && (
        <Typography color="text.secondary" sx={{ mt: 0.5 }}>
          Cancelled by the facilities office
        </Typography>
      )}
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1 }}>
        Reason
      </Typography>
      {reason ? (
        <Typography data-testid="cancel-reason" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
          {reason}
        </Typography>
      ) : (
        <Typography color="text.secondary">No reason given</Typography>
      )}
    </Card>
  )
}
