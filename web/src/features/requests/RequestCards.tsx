import type { ReactNode } from 'react'
import { Box, Chip, List, ListItem, ListItemText, Paper, Typography } from '@mui/material'
import type { BookingRequestDetailDto, RequestStatusHistoryDto } from '../../api/types'
import { formatCampusTimeRange, formatDateTime } from '../../ui/formatDateTime'
import { formatLkr } from '../../ui/formatLkr'
import { requestStatusLabel } from './requestStatus'

/** A titled section of a detail page; its title is the region's accessible name. */
export function Card({ title, wide = false, children }: { title: string; wide?: boolean; children: ReactNode }) {
  return (
    <Paper
      component="section"
      aria-label={title}
      variant="outlined"
      sx={{ p: 2, gridColumn: wide ? { md: '1 / -1' } : undefined, minWidth: 0 }}
    >
      <Typography variant="subtitle2" component="h2" color="text.secondary" sx={{ mb: 1 }}>
        {title}
      </Typography>
      {children}
    </Paper>
  )
}

export function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography component="div">{children}</Typography>
    </div>
  )
}

/** Everything the requester entered. Shared by the request detail and the approval detail. */
export function RequestCards({ request }: { request: BookingRequestDetailDto }) {
  return (
    <>
      <Card title="Requester">
        <Typography>{request.requester.name}</Typography>
        <Typography variant="body2" color="text.secondary">
          {request.requester.email}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {request.requester.role}
        </Typography>
        <Typography sx={{ mt: 1 }}>{request.club ? request.club.name : 'Academic booking'}</Typography>
      </Card>
      <Card title="Booking">
        <Box sx={{ display: 'grid', gap: 1 }}>
          <Fact label="When">{formatCampusTimeRange(request.requestedStart, request.requestedEnd)}</Fact>
          <Fact label="Attendees">{request.attendees}</Fact>
          <Fact label="Budget">{formatLkr(request.budgetLkr)}</Fact>
          <Fact label="Submitted">{formatDateTime(request.createdAt)}</Fact>
        </Box>
      </Card>
      <Card title="Required features">
        {request.requiredFeatures.length ? (
          <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap' }}>
            {request.requiredFeatures.map((f) => (
              <Chip key={f.code} size="small" variant="outlined" label={f.name} title={f.code} />
            ))}
          </Box>
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

/** Status changes oldest first. A change with no user was made by the system. Reasons are plain text. */
export function Timeline({ history }: { history: RequestStatusHistoryDto[] }) {
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
