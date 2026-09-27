import type { ReactNode } from 'react'
import { Link as RouterLink, useLocation, useParams } from 'react-router'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import {
  Alert,
  Box,
  Button,
  Chip,
  LinearProgress,
  List,
  ListItem,
  ListItemText,
  Paper,
  Stack,
  Typography,
} from '@mui/material'
import { parseProblem } from '../../api/problem'
import type { BookingRequestDetailDto, RequestStatusHistoryDto } from '../../api/types'
import { formatCampusTimeRange, formatDateTime } from '../../ui/formatDateTime'
import { formatLkr } from '../../ui/formatLkr'
import type { FromListState } from './BookingRequestsPage'
import { RequestStatusChip } from './RequestStatusChip'
import { requestStatusLabel } from './requestStatus'
import { useBookingRequest } from './useRequests'

const parseId = (raw: string | undefined) => (raw !== undefined && /^[1-9]\d*$/.test(raw) ? Number(raw) : undefined)

/** One booking request for a Facilities Officer (§12, Component C), read-only. Actions arrive in Phase 3. */
export function BookingRequestDetailPage() {
  const id = parseId(useParams().id)
  const location = useLocation()
  // Back to the list with the filters it had, when we came from it.
  const listSearch = (location.state as Partial<FromListState> | null)?.listSearch ?? ''
  const { data: request, isPending, isError, error, refetch } = useBookingRequest(id ?? 0)

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
      </Stack>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
        <RequestCards request={request} />
        <Card title="Status history" wide>
          <Timeline history={request.history} />
        </Card>
        <Card title="Agent proposal" wide>
          <Typography color="text.secondary">
            No agent proposal yet. The proposal, validation checklist and agent trace will appear here.
          </Typography>
        </Card>
      </Box>
    </>
  )
}

function Card({ title, wide = false, children }: { title: string; wide?: boolean; children: ReactNode }) {
  return (
    <Paper
      component="section"
      aria-label={title}
      variant="outlined"
      sx={{ p: 2, gridColumn: wide ? { md: '1 / -1' } : undefined }}
    >
      <Typography variant="subtitle2" component="h2" color="text.secondary" sx={{ mb: 1 }}>
        {title}
      </Typography>
      {children}
    </Paper>
  )
}

function RequestCards({ request }: { request: BookingRequestDetailDto }) {
  return (
    <>
      <Card title="Requester">
        <Typography>{request.requester.name}</Typography>
        <Typography variant="body2" color="text.secondary">
          {request.requester.email}
        </Typography>
        <Typography sx={{ mt: 1 }}>{request.club ? request.club.name : 'Academic booking'}</Typography>
      </Card>
      <Card title="Booking">
        <Stack spacing={1}>
          <Fact label="When">{formatCampusTimeRange(request.requestedStart, request.requestedEnd)}</Fact>
          <Fact label="Attendees">{request.attendees}</Fact>
          <Fact label="Budget">{formatLkr(request.budgetLkr)}</Fact>
          <Fact label="Submitted">{formatDateTime(request.createdAt)}</Fact>
        </Stack>
      </Card>
      <Card title="Required features">
        {request.requiredFeatures.length ? (
          <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {request.requiredFeatures.map((f) => (
              <Chip key={f.code} size="small" variant="outlined" label={f.name} title={f.code} />
            ))}
          </Stack>
        ) : (
          <Typography color="text.secondary">None</Typography>
        )}
      </Card>
      <Card title="Equipment">
        {request.equipment.length ? (
          <List dense disablePadding>
            {request.equipment.map((e) => (
              <ListItem key={e.typeId} disableGutters>
                <ListItemText primary={`${e.typeCode} — ${e.typeName} × ${e.quantity}`} />
              </ListItem>
            ))}
          </List>
        ) : (
          <Typography color="text.secondary">None</Typography>
        )}
      </Card>
      <Card title="Requester notes (as written by the requester)" wide>
        {/* Untrusted requester text: a plain React text child (escaped, never HTML or markdown), line breaks kept. */}
        {request.notes ? (
          <Typography data-testid="request-notes" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
            {request.notes}
          </Typography>
        ) : (
          <Typography color="text.secondary">None</Typography>
        )}
      </Card>
    </>
  )
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography>{children}</Typography>
    </div>
  )
}

/** Status changes oldest first. A change with no user was made by the system. */
function Timeline({ history }: { history: RequestStatusHistoryDto[] }) {
  return (
    <List dense disablePadding aria-label="Status timeline">
      {history.map((h, i) => (
        <ListItem key={`${h.changedAt}-${i}`} disableGutters alignItems="flex-start">
          <ListItemText
            primary={requestStatusLabel(h.toStatus)}
            secondary={
              <>
                {formatDateTime(h.changedAt)} · {h.changedById === null ? 'System' : (h.changedByName ?? 'Unknown user')}
                {h.reason && (
                  <Box component="span" sx={{ display: 'block', whiteSpace: 'pre-wrap' }}>
                    {h.reason}
                  </Box>
                )}
              </>
            }
          />
        </ListItem>
      ))}
    </List>
  )
}
