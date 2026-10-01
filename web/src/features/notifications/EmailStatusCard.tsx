import { Alert, Button, Chip, Skeleton, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { parseProblem } from '../../api/problem'
import { formatDateTime } from '../../ui/formatDateTime'
import { Card } from '../requests/RequestCards'
import { NOTIFICATION_KIND_LABELS, NOTIFICATION_STATUS_COLORS, useRequestNotifications } from './notifications'

/**
 * The emails sent to the requester about this request (UC27, Task 5.2): kind, status, when, and the error as plain text.
 * Read-only: there is no resend (a failed email may still have been delivered; the officer contacts the requester).
 */
export function EmailStatusCard({ requestId, version }: { requestId: number; version: string | undefined }) {
  const query = useRequestNotifications(requestId, version)
  return (
    <Card title="Emails to the requester" wide>
      <EmailStatusTable query={query} />
    </Card>
  )
}

function EmailStatusTable({ query }: { query: ReturnType<typeof useRequestNotifications> }) {
  if (query.isPending) return <Skeleton variant="rectangular" height={60} aria-label="Loading emails" />
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
        Could not load the emails: {parseProblem(query.error).title}
      </Alert>
    )
  if (!query.data.length) return <Typography color="text.secondary">No emails yet</Typography>
  return (
    <TableContainer>
      <Table size="small" aria-label="Emails">
        <TableHead>
          <TableRow>
            <TableCell>Email</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>To</TableCell>
            <TableCell>Time</TableCell>
            <TableCell align="right">Attempts</TableCell>
            <TableCell>Error</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {query.data.map((n) => (
            <TableRow key={n.id}>
              <TableCell>{NOTIFICATION_KIND_LABELS[n.kind] ?? n.kind}</TableCell>
              <TableCell>
                <Chip size="small" variant="outlined" color={NOTIFICATION_STATUS_COLORS[n.status] ?? 'default'} label={n.status} />
              </TableCell>
              <TableCell sx={{ overflowWrap: 'anywhere' }}>
                {n.recipient}
                {n.redirected && (
                  <Typography component="span" variant="caption" color="text.secondary">
                    {' '}
                    (redirected)
                  </Typography>
                )}
              </TableCell>
              <TableCell>{formatDateTime(n.sentAt ?? n.lastAttemptAt ?? n.createdAt)}</TableCell>
              <TableCell align="right">{n.attempts}</TableCell>
              <TableCell sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{n.error ?? ''}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}
