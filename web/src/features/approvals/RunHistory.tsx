import { Alert, Button, Chip, Skeleton, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import type { UseQueryResult } from '@tanstack/react-query'
import { parseProblem } from '../../api/problem'
import type { AgentRunSummaryDto } from '../../api/types'
import { formatDateTime } from '../../ui/formatDateTime'

const RUN_COLORS: Record<string, 'default' | 'primary' | 'success' | 'warning' | 'error'> = {
  Queued: 'default',
  Running: 'primary',
  AwaitingApproval: 'warning',
  Resuming: 'primary',
  Completed: 'success',
  Rejected: 'error',
  Failed: 'error',
  Cancelled: 'default',
}

/** Every agent run of a request, newest first, with its status, timings and failure reason. */
export function RunHistory({ query }: { query: UseQueryResult<AgentRunSummaryDto[]> }) {
  if (query.isPending) return <Skeleton variant="rectangular" height={80} aria-label="Loading agent runs" />
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
        Could not load the agent runs: {parseProblem(query.error).title}
      </Alert>
    )
  if (!query.data.length) return <Typography color="text.secondary">No agent runs yet</Typography>
  return (
    <TableContainer>
      <Table size="small" aria-label="Agent runs">
        <TableHead>
          <TableRow>
            <TableCell>Revision</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>Started</TableCell>
            <TableCell align="right">Duration</TableCell>
            <TableCell>Failure reason</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {query.data.map((run) => (
            <TableRow key={run.id}>
              <TableCell>{run.revisionNo}</TableCell>
              <TableCell>
                <Chip size="small" variant="outlined" color={RUN_COLORS[run.status] ?? 'default'} label={run.status} />
              </TableCell>
              <TableCell>{formatDateTime(run.startedAt ?? run.createdAt)}</TableCell>
              <TableCell align="right">{run.durationMs === null ? '—' : `${run.durationMs} ms`}</TableCell>
              <TableCell sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{run.failureReason ?? ''}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}
