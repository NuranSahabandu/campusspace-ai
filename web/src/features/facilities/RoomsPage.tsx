import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import AddIcon from '@mui/icons-material/Add'
import BlockIcon from '@mui/icons-material/Block'
import EditIcon from '@mui/icons-material/Edit'
import {
  Autocomplete,
  Button,
  Chip,
  FormControl,
  FormControlLabel,
  InputLabel,
  Link,
  MenuItem,
  Select,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material'
import { GridActionsCellItem, type GridColDef } from '@mui/x-data-grid'
import { api } from '../../api/client'
import type { RoomDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'
import { useServerTable } from '../../hooks/useServerTable'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { FeatureChips } from './FeatureChips'
import { RoomFormDialog } from './RoomFormDialog'
import { ROOM_TYPES, roomTypeLabel } from './roomTypes'
import { ROOMS_SORT_FIELDS, roomsKeys, useBuildings, useFeatures, useRooms } from './useFacilities'

// A positive whole number, or nothing (so the filter is not sent).
const toMinCapacity = (value: string) => {
  const n = Number(value)
  return value.trim() !== '' && Number.isInteger(n) && n > 0 ? n : undefined
}

/** Facilities Officer list of rooms (§12, UC13): filters, sort and paging on the server. */
export function RoomsPage() {
  const table = useServerTable({ sortFields: ROOMS_SORT_FIELDS, initialSort: [{ field: 'code', sort: 'asc' }] })
  const buildings = useBuildings()
  const features = useFeatures()
  const [buildingId, setBuildingId] = useState('')
  const [type, setType] = useState('')
  const [minCapacity, setMinCapacity] = useState('')
  const [featureCodes, setFeatureCodes] = useState<string[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const debouncedMinCapacity = useDebouncedValue(minCapacity, 300)
  const query = useRooms({
    ...table.params,
    buildingId: buildingId ? Number(buildingId) : undefined,
    type: type || undefined,
    minCapacity: toMinCapacity(debouncedMinCapacity),
    features: featureCodes.length ? featureCodes.join(',') : undefined,
    includeInactive: includeInactive || undefined,
  })
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<RoomDto | null>(null)
  const [deactivating, setDeactivating] = useState<RoomDto | null>(null)

  const deactivate = useApiMutation<number>({
    mutationFn: (id) => api.delete(`/api/rooms/${id}`),
    invalidate: [roomsKeys.all],
    successMessage: 'Room deactivated',
    onSuccess: () => setDeactivating(null),
  })

  // Every filter change starts again from the first page.
  const onFilter =
    <T,>(set: (value: T) => void) =>
    (value: T) => {
      set(value)
      table.resetPage()
    }

  const columns: GridColDef<RoomDto>[] = [
    {
      field: 'code',
      headerName: 'Code',
      width: 110,
      renderCell: ({ row }) => (
        <Link component={RouterLink} to={`/rooms/${row.id}`}>
          {row.code}
        </Link>
      ),
    },
    { field: 'name', headerName: 'Name', flex: 1, minWidth: 180 },
    {
      field: 'building',
      headerName: 'Building',
      width: 110,
      valueGetter: (_value, row) => row.building.code,
    },
    { field: 'type', headerName: 'Type', width: 140, sortable: false, valueFormatter: (value: string) => roomTypeLabel(value) },
    { field: 'capacity', headerName: 'Capacity', type: 'number', width: 100 },
    {
      field: 'features',
      headerName: 'Features',
      flex: 1.5,
      minWidth: 220,
      sortable: false,
      renderCell: ({ row }) => <FeatureChips features={row.features} />,
    },
    {
      field: 'isActive',
      headerName: 'Status',
      width: 110,
      sortable: false,
      renderCell: ({ value }) => (
        <Chip size="small" label={value ? 'Active' : 'Inactive'} color={value ? 'success' : 'default'} />
      ),
    },
    {
      field: 'actions',
      type: 'actions',
      headerName: 'Actions',
      width: 100,
      // An inactive room is reactivated from Edit (the Active switch).
      getActions: ({ row }) => [
        <GridActionsCellItem key="edit" icon={<EditIcon />} label={`Edit ${row.code}`} onClick={() => setEditing(row)} />,
        ...(row.isActive
          ? [
              <GridActionsCellItem
                key="deactivate"
                icon={<BlockIcon />}
                label={`Deactivate ${row.code}`}
                onClick={() => setDeactivating(row)}
              />,
            ]
          : []),
      ],
    },
  ]

  return (
    <>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h4" component="h1">
          Rooms
        </Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
          New room
        </Button>
      </Stack>

      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <TextField
          label="Search code or name"
          value={table.search}
          onChange={(e) => table.setSearch(e.target.value)}
          size="small"
          sx={{ minWidth: 220 }}
        />
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="building-filter-label">Building</InputLabel>
          <Select
            labelId="building-filter-label"
            label="Building"
            value={buildingId}
            onChange={(e) => onFilter(setBuildingId)(e.target.value)}
          >
            <MenuItem value="">All buildings</MenuItem>
            {(buildings.data ?? []).map((b) => (
              <MenuItem key={b.id} value={String(b.id)}>
                {b.code} · {b.name}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="type-filter-label">Type</InputLabel>
          <Select labelId="type-filter-label" label="Type" value={type} onChange={(e) => onFilter(setType)(e.target.value)}>
            <MenuItem value="">All types</MenuItem>
            {ROOM_TYPES.map((t) => (
              <MenuItem key={t} value={t}>
                {roomTypeLabel(t)}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <TextField
          label="Min capacity"
          type="number"
          value={minCapacity}
          onChange={(e) => onFilter(setMinCapacity)(e.target.value)}
          size="small"
          slotProps={{ htmlInput: { min: 1 } }}
          sx={{ width: 130 }}
        />
        <Autocomplete
          multiple
          size="small"
          options={(features.data ?? []).map((f) => f.code)}
          value={featureCodes}
          onChange={(_, value) => onFilter(setFeatureCodes)(value)}
          renderInput={(params) => <TextField {...params} label="Features (all of)" />}
          sx={{ minWidth: 260 }}
        />
        <FormControlLabel
          label="Include inactive"
          control={
            <Switch checked={includeInactive} onChange={(e) => onFilter(setIncludeInactive)(e.target.checked)} />
          }
        />
      </Stack>

      <ServerDataGrid query={query} columns={columns} gridProps={table.gridProps} noun="rooms" emptyText="No rooms match" />

      {creating && <RoomFormDialog onClose={() => setCreating(false)} />}
      {editing && <RoomFormDialog room={editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={deactivating !== null}
        title="Deactivate room?"
        message={`Deactivate ${deactivating?.code ?? ''}? It is hidden from requesters and the agents until you reactivate it.`}
        confirmLabel="Deactivate"
        destructive
        pending={deactivate.isPending}
        onConfirm={() => deactivating && deactivate.mutate(deactivating.id)}
        onClose={() => setDeactivating(null)}
      />
    </>
  )
}
