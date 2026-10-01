import { Link as RouterLink, useParams } from 'react-router'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import { Alert, Box, Button, Chip, LinearProgress, Stack, Typography } from '@mui/material'
import { parseProblem } from '../../api/problem'
import type { AgentRunDetailDto } from '../../api/types'
import { formatDateTime } from '../../ui/formatDateTime'
import { AgentTimeline, JsonText } from '../approvals/AgentTimeline'
import { RUN_STATUS_COLORS } from '../approvals/agentRuns'
import { PolicyChangedBanner } from '../approvals/PolicyChangedBanner'
import { ProposalCard } from '../approvals/ProposalCard'
import { ValidationChecklist } from '../approvals/ValidationChecklist'
import { Card, Fact } from '../requests/RequestCards'
import { formatMs } from './format'
import { useAgentRunDetail } from './useAgentRuns'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * One agent run's full trace (the addendum's "run detail shows the policy snapshot"): status and timings, the proposal,
 * the validation checklist, the agent timeline, the officer's decisions and the policy snapshot the agents used.
 * Officer text, the summary and the failure reason are plain text. While the run is live the page re-fetches.
 */
export function AgentRunDetailPage() {
  const raw = useParams().runId
  const runId = raw !== undefined && GUID.test(raw) ? raw : undefined
  const query = useAgentRunDetail(runId)

  const back = (
    <Button component={RouterLink} to="/agent-runs" startIcon={<ArrowBackIcon />} sx={{ mb: 1 }}>
      Back to agent runs
    </Button>
  )
  const notFound = (
    <>
      {back}
      <Alert severity="warning">Agent run not found.</Alert>
    </>
  )

  if (runId === undefined) return notFound
  if (query.isPending) return <LinearProgress aria-label="Loading agent run" />
  if (query.isError) {
    const problem = parseProblem(query.error)
    if (problem.status === 404) return notFound
    return (
      <>
        {back}
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => query.refetch()}>
              Retry
            </Button>
          }
        >
          Could not load the agent run: {problem.title}
        </Alert>
      </>
    )
  }

  return (
    <>
      {back}
      <RunView run={query.data} />
    </>
  )
}

function RunView({ run }: { run: AgentRunDetailDto }) {
  const awaiting = run.status === 'AwaitingApproval'
  return (
    <>
      <Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 2 }}>
        <Typography variant="h4" component="h1">
          Agent run · revision {run.revisionNo}
        </Typography>
        <Chip variant="outlined" color={RUN_STATUS_COLORS[run.status] ?? 'default'} label={run.status} />
        <Stack direction="row" spacing={1} useFlexGap sx={{ ml: 'auto', flexWrap: 'wrap' }}>
          {awaiting && (
            <Button variant="contained" component={RouterLink} to={`/approvals/${run.requestId}`}>
              Review proposal
            </Button>
          )}
          <Button variant="outlined" component={RouterLink} to={`/requests/${run.requestId}`}>
            Open request #{run.requestId}
          </Button>
        </Stack>
      </Stack>

      <Stack spacing={2} sx={{ mb: 2 }}>
        {/* Only while the proposal still waits for a decision: for a finished run the warning would be misleading. */}
        {awaiting && <PolicyChangedBanner keys={run.policyChangedKeys} />}
        {run.failureReason && (
          <Alert severity="error" aria-label="Failure reason">
            <Box sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{run.failureReason}</Box>
          </Alert>
        )}
      </Stack>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
        <Card title="Run">
          <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))' }}>
            <Fact label="Created">{formatDateTime(run.createdAt)}</Fact>
            <Fact label="Started">{run.startedAt ? formatDateTime(run.startedAt) : '—'}</Fact>
            <Fact label="Completed">{run.completedAt ? formatDateTime(run.completedAt) : '—'}</Fact>
            <Fact label="Wall time (incl. officer wait)">{formatMs(run.durationMs)}</Fact>
            <Fact label="Model">{run.model ?? '—'}</Fact>
            <Fact label="Nodes">{run.nodes.length ? run.nodes.join(' → ') : '—'}</Fact>
          </Box>
        </Card>
        <Card title="Officer summary">
          {run.officerSummary ? (
            <Typography sx={{ whiteSpace: 'pre-wrap' }}>{run.officerSummary}</Typography>
          ) : (
            <Typography color="text.secondary">No summary</Typography>
          )}
        </Card>
        <Card title="Proposal" wide>
          {run.proposal ? <ProposalCard proposal={run.proposal} /> : <Typography color="text.secondary">No proposal</Typography>}
        </Card>
        <Card title="Validation checklist">
          {run.validation.length ? (
            <ValidationChecklist validation={run.validation} />
          ) : (
            <Typography color="text.secondary">No validation results</Typography>
          )}
        </Card>
        <Card title="Decisions">
          {run.decisions.length ? (
            <Stack spacing={1}>
              {run.decisions.map((d) => (
                <Box key={`${d.decidedAt}-${d.decision}`}>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    {d.decision} · {d.officerName} · {formatDateTime(d.decidedAt)}
                  </Typography>
                  {d.comment && (
                    <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                      {d.comment}
                    </Typography>
                  )}
                </Box>
              ))}
            </Stack>
          ) : (
            <Typography color="text.secondary">No decisions</Typography>
          )}
        </Card>
        <Card title="Agent timeline" wide>
          {run.steps.length ? <AgentTimeline run={run} /> : <Typography color="text.secondary">No steps recorded</Typography>}
        </Card>
        <Card title="Policy snapshot" wide>
          {run.policySnapshot === null ? (
            <Typography color="text.secondary">No policy snapshot</Typography>
          ) : (
            <JsonText value={run.policySnapshot} label="Policy snapshot" />
          )}
        </Card>
      </Box>
    </>
  )
}
