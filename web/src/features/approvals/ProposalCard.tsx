import { Alert, Box, Chip, List, ListItem, ListItemText, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import type { AgentProposalDto, VenueOptionDto } from '../../api/types'

/** How each equipment source reads to the officer, and its chip colour. */
const EQUIPMENT_SOURCES: Record<string, { label: string; color: 'default' | 'info' | 'warning' }> = {
  portable: { label: 'Portable', color: 'default' },
  room_builtin: { label: 'Built into the room', color: 'info' },
  substitute: { label: 'Substitute', color: 'warning' },
}

export function SourceChip({ source }: { source: string }) {
  const style = EQUIPMENT_SOURCES[source] ?? { label: source, color: 'default' as const }
  return <Chip size="small" variant="outlined" color={style.color} label={style.label} />
}

function roomLine(room: VenueOptionDto) {
  const facts = [room.capacity !== null ? `${room.capacity} seats` : null, room.building].filter(Boolean).join(' · ')
  return `${room.code} · ${room.name}${facts ? ` (${facts})` : ''}`
}

/**
 * The agents' proposal: the chosen room (from the proposal's room_id) and its reason, the alternatives, and the
 * equipment lines with their source. Every agent text is rendered as plain text. Prices are not here: the quote card
 * shows the .NET quotation.
 */
export function ProposalCard({ proposal }: { proposal: AgentProposalDto }) {
  const unmet = [...(proposal.venueUnmet ? [proposal.venueUnmet] : []), ...proposal.equipmentUnmet]
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <div>
        <Typography variant="overline" color="text.secondary">
          Chosen room
        </Typography>
        <Typography variant="h6" component="p" data-testid="chosen-room">
          {roomLine(proposal.chosen)}
        </Typography>
        <Typography color="text.secondary">{proposal.chosen.reason ?? 'No reason given by the agent.'}</Typography>
      </div>

      <div>
        <Typography variant="overline" color="text.secondary">
          Alternatives
        </Typography>
        {proposal.alternatives.length ? (
          <List dense disablePadding aria-label="Alternative rooms">
            {proposal.alternatives.map((room) => (
              <ListItem key={room.roomId} disableGutters>
                <ListItemText primary={roomLine(room)} secondary={room.reason} />
              </ListItem>
            ))}
          </List>
        ) : (
          <Typography color="text.secondary">None</Typography>
        )}
      </div>

      <div>
        <Typography variant="overline" color="text.secondary">
          Equipment
        </Typography>
        {proposal.equipmentLines.length ? (
          <Table size="small" aria-label="Proposed equipment">
            <TableHead>
              <TableRow>
                <TableCell>Type</TableCell>
                <TableCell align="right">Qty</TableCell>
                <TableCell>Source</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {proposal.equipmentLines.map((line) => (
                <TableRow key={`${line.typeCode}-${line.source}`}>
                  <TableCell>{line.typeCode}</TableCell>
                  <TableCell align="right">{line.source === 'room_builtin' ? '—' : line.qty}</TableCell>
                  <TableCell>
                    <SourceChip source={line.source} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <Typography color="text.secondary">No equipment</Typography>
        )}
        {proposal.substitutions.map((s) => (
          <Typography key={`${s.requestedCode}-${s.substituteCode}`} variant="body2" sx={{ mt: 1 }}>
            {s.substituteCode} × {s.qty} instead of {s.requestedCode}: {s.reason}
          </Typography>
        ))}
      </div>

      {unmet.length > 0 && (
        <Alert severity="warning" aria-label="Unmet items">
          Not met:
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {unmet.map((u) => (
              <li key={u}>{u}</li>
            ))}
          </Box>
        </Alert>
      )}

      {proposal.policyFlags.length > 0 && (
        <div>
          <Typography variant="overline" color="text.secondary">
            Policy notes
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {proposal.policyFlags.map((flag) => (
              <Typography component="li" variant="body2" key={flag}>
                {flag}
              </Typography>
            ))}
          </Box>
        </div>
      )}
    </Box>
  )
}
