import { useState } from 'react'
import AddIcon from '@mui/icons-material/Add'
import DeleteIcon from '@mui/icons-material/Delete'
import { Alert, Button, Chip, FormControlLabel, Stack, Switch, Typography } from '@mui/material'
import { GridActionsCellItem, type GridColDef } from '@mui/x-data-grid'
import { api } from '../../api/client'
import type { BlackoutDto, RoomDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { useServerTable } from '../../hooks/useServerTable'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { formatDateTime } from '../../ui/formatDateTime'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { BlackoutClashesDialog } from './BlackoutClashesDialog'
import { BlackoutFormDialog } from './BlackoutFormDialog'
import { BLACKOUTS_SORT_FIELDS, roomsKeys, useBlackouts } from './useFacilities'

/** A room's maintenance blackouts (UC14): upcoming by default, or all of them with "Show past". */
export function BlackoutsSection({ room }: { room: RoomDto }) {
  const table = useServerTable({ sortFields: BLACKOUTS_SORT_FIELDS, initialSort: [{ field: 'start', sort: 'asc' }], pageSize: 10 })
  // Fixed at mount so the query key (and the list) does not change on every render.
  const [now] = useState(() => new Date().toISOString())
  const [showPast, setShowPast] = useState(false)
  // "Upcoming" means not yet over: the API returns blackouts that overlap [from, ∞).
  const query = useBlackouts(room.id, { ...table.params, search: undefined, from: showPast ? undefined : now })
  const [adding, setAdding] = useState(false)
  const [deleting, setDeleting] = useState<BlackoutDto | null>(null)
  const [viewingClashes, setViewingClashes] = useState<BlackoutDto | null>(null)

  const remove = useApiMutation<number>({
    mutationFn: (id) => api.delete(`/api/rooms/${room.id}/blackouts/${id}`),
    invalidate: [roomsKeys.all],
    successMessage: 'Blackout deleted',
    onSuccess: () => setDeleting(null),
  })

  const columns: GridColDef<BlackoutDto>[] = [
    { field: 'start', headerName: 'Start', width: 190, valueFormatter: (value: string) => formatDateTime(value) },
    { field: 'end', headerName: 'End', width: 190, sortable: false, valueFormatter: (value: string) => formatDateTime(value) },
    { field: 'reason', headerName: 'Reason', flex: 1, minWidth: 200, sortable: false },
    { field: 'createdByName', headerName: 'Created by', width: 170, sortable: false },
    {
      field: 'clashCount',
      headerName: 'Clashes',
      width: 120,
      sortable: false,
      // Active bookings the blackout clashes with now; the chip opens them so the officer can cancel each one.
      renderCell: ({ row }) =>
        row.clashCount > 0 ? (
          <Chip
            size="small"
            color="warning"
            label={`${row.clashCount} clash${row.clashCount === 1 ? '' : 'es'}`}
            onClick={() => setViewingClashes(row)}
            aria-label={`Show ${row.clashCount} clashing booking${row.clashCount === 1 ? '' : 's'} for ${formatDateTime(row.start)}`}
          />
        ) : (
          <Typography variant="body2" color="text.secondary" component="span">
            None
          </Typography>
        ),
    },
    {
      field: 'actions',
      type: 'actions',
      headerName: 'Actions',
      width: 90,
      getActions: ({ row }) => [
        <GridActionsCellItem
          key="delete"
          icon={<DeleteIcon />}
          label={`Delete blackout ${formatDateTime(row.start)}`}
          onClick={() => setDeleting(row)}
        />,
      ],
    },
  ]

  return (
    <Stack component="section" aria-label="Blackouts" spacing={1.5} sx={{ mt: 4 }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h5" component="h2">
          Blackouts
        </Typography>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <FormControlLabel
            label="Show past"
            control={
              <Switch
                checked={showPast}
                onChange={(e) => {
                  setShowPast(e.target.checked)
                  table.resetPage()
                }}
              />
            }
          />
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setAdding(true)}>
            Add blackout
          </Button>
        </Stack>
      </Stack>
      <Alert severity="info">
        Adding a blackout lists the active bookings it clashes with. They are not cancelled automatically: open a
        blackout's clashes to cancel them.
      </Alert>

      <ServerDataGrid
        query={query}
        columns={columns}
        gridProps={table.gridProps}
        noun="blackouts"
        emptyText={showPast ? 'No blackouts' : 'No upcoming blackouts'}
        height={420}
      />

      {adding && <BlackoutFormDialog room={room} onClose={() => setAdding(false)} />}
      {viewingClashes && <BlackoutClashesDialog blackout={viewingClashes} onClose={() => setViewingClashes(null)} />}
      <ConfirmDialog
        open={deleting !== null}
        title="Delete blackout?"
        message={
          deleting
            ? `Delete the blackout ${formatDateTime(deleting.start)} – ${formatDateTime(deleting.end)} (${deleting.reason})?`
            : ''
        }
        confirmLabel="Delete"
        destructive
        pending={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </Stack>
  )
}
