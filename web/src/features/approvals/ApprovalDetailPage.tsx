import { type ReactNode, useState } from 'react'
import { Link as RouterLink, useParams } from 'react-router'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import { useQueryClient } from '@tanstack/react-query'
import { Alert, AlertTitle, Box, Button, LinearProgress, Skeleton, Stack, Typography } from '@mui/material'
import { parseProblem } from '../../api/problem'
import type { BookingRequestDetailDto } from '../../api/types'
import { toast } from '../../ui/toastStore'
import { QuoteTable } from '../quotations/QuoteTable'
import { Card, RequestCards, Timeline } from '../requests/RequestCards'
import { RequestStatusChip } from '../requests/RequestStatusChip'
import { RequestStatuses } from '../requests/requestStatus'
import { bookingRequestsKeys, useBookingRequest } from '../requests/useRequests'
import { AgentTimeline } from './AgentTimeline'
import { DecisionDialog, type DecisionMode, type DecisionResult } from './DecisionDialog'
import { PolicyChangedBanner } from './PolicyChangedBanner'
import { ProposalCard } from './ProposalCard'
import { EmailStatusCard } from '../notifications/EmailStatusCard'
import { RunHistory } from './RunHistory'
import { APPROVAL_IN_PROGRESS_REFRESH_MS, approvalsKeys, useAgentRun, useRequestQuotation, useRequestRuns } from './useApprovals'
import { ValidationChecklist } from './ValidationChecklist'

const parseId = (raw: string | undefined) => (raw !== undefined && /^[1-9]\d*$/.test(raw) ? Number(raw) : undefined)

const PREPARING = [RequestStatuses.AgentProcessing, RequestStatuses.RevisionRequested] as readonly string[]

/**
 * The officer's approval detail (UC18–UC21, plan §12 centrepiece): the request, the latest run's proposal, the .NET
 * quotation, the validation checklist, the agent timeline and the run history, with Approve / Reject / Request revision
 * while the request is PendingApproval and its live run is AwaitingApproval.
 *
 * Results: approve 200 shows Approved; approve 202 shows "Approval in progress…" and re-fetches every 3 s until the
 * request leaves PendingApproval; revise 202 shows "New proposal being prepared" and follows refreshIntervalFor; a 409
 * shows the server's message in an alert and everything is re-fetched (a new proposal, or the request was closed).
 */
export function ApprovalDetailPage() {
  const id = parseId(useParams().requestId)
  const queryClient = useQueryClient()
  const [approvalPending, setApprovalPending] = useState(false)
  const [conflict, setConflict] = useState<string | null>(null)
  const [dialog, setDialog] = useState<DecisionMode | null>(null)

  const requestQuery = useBookingRequest(id ?? 0, {
    refreshMs: approvalPending ? APPROVAL_IN_PROGRESS_REFRESH_MS : false,
  })
  const request = requestQuery.data
  // The approval is finished (or failed) once the request leaves PendingApproval: stop the fast refresh.
  if (approvalPending && request && request.status !== RequestStatuses.PendingApproval) setApprovalPending(false)

  const version = request?.updatedAt
  const runs = useRequestRuns(id ?? 0, version)
  const latestRun = runs.data?.[0]
  const runQuery = useAgentRun(latestRun?.id, version)
  const quoteQuery = useRequestQuotation(id ?? 0, version)
  // A disabled query (no run yet) stays "pending": loading is only while something is really being fetched.
  const run = {
    data: runQuery.data,
    loading: runs.isPending || (latestRun !== undefined && runQuery.isPending),
    isError: runs.isError || runQuery.isError,
    error: runs.error ?? runQuery.error,
    refetch: () => (runs.isError ? runs.refetch() : runQuery.refetch()),
  }
  const quote = {
    data: quoteQuery.data,
    loading: quoteQuery.isPending,
    isError: quoteQuery.isError,
    error: quoteQuery.error,
    refetch: () => quoteQuery.refetch(),
  }

  const back = (
    <Button component={RouterLink} to="/approvals" startIcon={<ArrowBackIcon />} sx={{ mb: 1 }}>
      Back to approvals
    </Button>
  )
  const notFound = (
    <>
      {back}
      <Alert severity="warning">Request not found. It may not exist, or you may not have access to it.</Alert>
    </>
  )

  if (id === undefined) return notFound
  if (requestQuery.isPending) return <LinearProgress aria-label="Loading request" />
  if (requestQuery.isError) {
    const problem = parseProblem(requestQuery.error)
    if (problem.status === 404 || problem.status === 403) return notFound
    return (
      <>
        {back}
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => requestQuery.refetch()}>
              Retry
            </Button>
          }
        >
          Could not load the request: {problem.title}
        </Alert>
      </>
    )
  }
  if (request === undefined) return <LinearProgress aria-label="Loading request" />

  const pendingApproval = request.status === RequestStatuses.PendingApproval
  const approving = approvalPending && pendingApproval
  const canDecide = pendingApproval && latestRun?.status === 'AwaitingApproval' && !approving

  const onDone = ({ mode, status }: DecisionResult) => {
    setConflict(null)
    if (mode === 'approve' && status === 202) setApprovalPending(true)
    else if (mode === 'approve') toast.success('Request approved')
    else if (mode === 'reject') toast.success('Request rejected')
    else toast.success('Revision requested: a new proposal is being prepared')
  }

  const onConflict = (message: string) => {
    setConflict(message)
    void queryClient.invalidateQueries({ queryKey: bookingRequestsKeys.all })
    void queryClient.invalidateQueries({ queryKey: approvalsKeys.all })
  }

  return (
    <>
      {back}
      <Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 2 }}>
        <Typography variant="h4" component="h1">
          {request.purpose}
        </Typography>
        <RequestStatusChip status={request.status} size="medium" />
        {canDecide && (
          <Stack direction="row" spacing={1} useFlexGap sx={{ ml: 'auto', flexWrap: 'wrap' }}>
            <Button variant="contained" color="success" onClick={() => setDialog('approve')} disabled={dialog !== null}>
              Approve
            </Button>
            <Button variant="outlined" onClick={() => setDialog('revise')} disabled={dialog !== null}>
              Request revision
            </Button>
            <Button variant="outlined" color="error" onClick={() => setDialog('reject')} disabled={dialog !== null}>
              Reject
            </Button>
          </Stack>
        )}
      </Stack>

      <Stack spacing={2} sx={{ mb: 2 }}>
        {conflict && (
          <Alert severity="error" onClose={() => setConflict(null)} data-testid="decision-conflict">
            {conflict}
          </Alert>
        )}
        {approving && (
          <Alert severity="info" data-testid="approval-in-progress">
            Approval in progress… The booking is being confirmed; this page updates every few seconds.
          </Alert>
        )}
        <StatusBanner request={request} />
        {run.data && pendingApproval && <PolicyChangedBanner keys={run.data.policyChangedKeys} />}
      </Stack>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
        <RequestCards request={request} />
        <Card title={latestRun ? `Proposal (revision ${latestRun.revisionNo})` : 'Proposal'} wide>
          <Section query={run} noun="proposal" empty="No proposal yet">
            {(data) => (data.proposal ? <ProposalCard proposal={data.proposal} /> : null)}
          </Section>
        </Card>
        <Card title="Quotation">
          <Section query={quote} noun="quotation" empty="No quotation yet">
            {(data) => <QuoteTable quote={data} />}
          </Section>
        </Card>
        <Card title="Validation checklist">
          <Section query={run} noun="checklist" empty="No validation results yet">
            {(data) => <ValidationChecklist validation={data.validation} />}
          </Section>
        </Card>
        <Card title="Agent timeline" wide>
          <Section query={run} noun="agent timeline" empty="No agent run yet">
            {(data) => <AgentTimeline run={data} />}
          </Section>
        </Card>
        <Card title="Agent runs" wide>
          <RunHistory query={runs} />
        </Card>
        <Card title="Status history" wide>
          <Timeline history={request.history} />
        </Card>
        <EmailStatusCard requestId={request.id} version={version} />
      </Box>

      {dialog && (
        <DecisionDialog
          mode={dialog}
          requestId={request.id}
          onClose={() => setDialog(null)}
          onDone={onDone}
          onConflict={onConflict}
        />
      )}
    </>
  )
}

/** What happened to a request that is not waiting for a decision. Reasons are shown as plain text. */
function StatusBanner({ request }: { request: BookingRequestDetailDto }) {
  const last = request.history.at(-1)
  if (PREPARING.includes(request.status))
    return (
      <Alert severity="info" data-testid="preparing">
        New proposal being prepared… This page updates when it is ready.
      </Alert>
    )
  if (request.status === RequestStatuses.Approved)
    return <Alert severity="success">Approved: the booking is confirmed and the quotation issued.</Alert>
  if (request.status === RequestStatuses.Rejected)
    return (
      <Alert severity="error">
        <AlertTitle>{last?.changedById === null ? 'Closed automatically' : 'Rejected'}</AlertTitle>
        <Box sx={{ whiteSpace: 'pre-wrap' }}>{last?.reason ?? 'No reason given'}</Box>
      </Alert>
    )
  if (request.status === RequestStatuses.AgentFailed)
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" component={RouterLink} to={`/requests/${request.id}`}>
            Open request
          </Button>
        }
      >
        The agents could not prepare a proposal: {last?.reason ?? 'no reason given'}. Retry from the request page.
      </Alert>
    )
  if (request.status !== RequestStatuses.PendingApproval)
    return <Alert severity="info">This request is no longer waiting for approval.</Alert>
  return null
}

interface SectionQuery<T> {
  data: T | null | undefined
  loading: boolean
  isError: boolean
  error: unknown
  refetch: () => unknown
}

/** Loading skeleton, error with Retry, and empty state for one card's query. */
function Section<T>({
  query,
  noun,
  empty,
  children,
}: {
  query: SectionQuery<T>
  noun: string
  empty: string
  children: (data: T) => ReactNode
}) {
  if (query.isError)
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={() => query.refetch()}>
            Retry
          </Button>
        }
      >
        Could not load the {noun}: {parseProblem(query.error).title}
      </Alert>
    )
  if (query.loading && query.data === undefined)
    return <Skeleton variant="rectangular" height={120} aria-label={`Loading ${noun}`} />
  const content = query.data ? children(query.data) : null
  return content ?? <Typography color="text.secondary">{empty}</Typography>
}
