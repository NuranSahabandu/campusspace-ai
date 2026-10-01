import { useState } from 'react'
import {
  Chip,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TableSortLabel,
  Typography,
} from '@mui/material'
import type { RoomUtilizationDto } from '../../api/types'
import { formatHours, formatUtilization } from './format'
import { type Direction, type SortKey, sortRooms } from './roomSort'

const COLUMNS: { key: SortKey; label: string; numeric: boolean }[] = [
  { key: 'room', label: 'Room', numeric: false },
  { key: 'building', label: 'Building', numeric: false },
  { key: 'booked', label: 'Booked', numeric: true },
  { key: 'available', label: 'Available', numeric: true },
  { key: 'utilization', label: 'Utilization', numeric: true },
]

/** Utilization per room (UC22), sortable on every column. Inactive rooms are flagged: they are not in the totals. */
export function RoomUtilizationTable({ rooms }: { rooms: RoomUtilizationDto[] }) {
  const [sort, setSort] = useState<{ key: SortKey; direction: Direction }>({ key: 'utilization', direction: 'desc' })
  const sorted = sortRooms(rooms, sort.key, sort.direction)

  const onSort = (key: SortKey) =>
    setSort((s) =>
      s.key === key
        ? { key, direction: s.direction === 'asc' ? 'desc' : 'asc' }
        : { key, direction: key === 'room' || key === 'building' ? 'asc' : 'desc' },
    )

  return (
    <Paper component="section" aria-label="Utilization by room" variant="outlined" sx={{ p: 2, minWidth: 0 }}>
      <Typography variant="subtitle1" component="h2" gutterBottom>
        Utilization by room
      </Typography>
      {rooms.length === 0 ? (
        <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
          No rooms.
        </Typography>
      ) : (
        <TableContainer>
          <Table size="small" aria-label="Utilization by room">
            <TableHead>
              <TableRow>
                {COLUMNS.map((c) => (
                  <TableCell
                    key={c.key}
                    align={c.numeric ? 'right' : 'left'}
                    sortDirection={sort.key === c.key ? sort.direction : false}
                  >
                    <TableSortLabel
                      active={sort.key === c.key}
                      direction={sort.key === c.key ? sort.direction : 'asc'}
                      onClick={() => onSort(c.key)}
                    >
                      {c.label}
                    </TableSortLabel>
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {sorted.map((r) => (
                <TableRow key={r.roomId}>
                  <TableCell>
                    {r.code} · {r.name}
                    {!r.isActive && (
                      <Chip size="small" variant="outlined" label="Inactive, not in totals" sx={{ ml: 1 }} />
                    )}
                  </TableCell>
                  <TableCell>{r.buildingCode}</TableCell>
                  <TableCell align="right">{formatHours(r.figures.bookedHours)}</TableCell>
                  <TableCell align="right">{formatHours(r.figures.availableHours)}</TableCell>
                  <TableCell align="right">{formatUtilization(r.figures)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </Paper>
  )
}
