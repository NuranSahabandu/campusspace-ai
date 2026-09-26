import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import AddIcon from '@mui/icons-material/Add'
import EditIcon from '@mui/icons-material/Edit'
import { Button, Chip, FormControlLabel, Link, Stack, Switch, TextField, Typography } from '@mui/material'
import { GridActionsCellItem, type GridColDef } from '@mui/x-data-grid'
import type { ClubDto } from '../../api/types'
import { useServerTable } from '../../hooks/useServerTable'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { ClubFormDialog } from './ClubFormDialog'
import { CLUBS_SORT_FIELDS, useClubs } from './useClubs'

/** Admin list of clubs (§12). Name opens the detail page, where members and the representative are managed. */
export function ClubsPage() {
  const table = useServerTable({ sortFields: CLUBS_SORT_FIELDS })
  const [includeInactive, setIncludeInactive] = useState(false)
  // Send the flag only when set, so the URL carries only what is set.
  const query = useClubs({ ...table.params, includeInactive: includeInactive || undefined })
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<ClubDto | null>(null)

  const columns: GridColDef<ClubDto>[] = [
    {
      field: 'name',
      headerName: 'Name',
      flex: 1,
      minWidth: 200,
      renderCell: ({ row }) => (
        <Link component={RouterLink} to={`/clubs/${row.id}`}>
          {row.name}
        </Link>
      ),
    },
    {
      field: 'representativeName',
      headerName: 'Representative',
      flex: 1,
      minWidth: 180,
      sortable: false,
      valueFormatter: (value: string | null) => value ?? '—',
    },
    { field: 'memberCount', headerName: 'Members', type: 'number', width: 110, sortable: false },
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
      width: 90,
      getActions: ({ row }) => [
        <GridActionsCellItem key="edit" icon={<EditIcon />} label={`Edit ${row.name}`} onClick={() => setEditing(row)} />,
      ],
    },
  ]

  return (
    <>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h4" component="h1">
          Clubs
        </Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
          New club
        </Button>
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 2, alignItems: { sm: 'center' } }}>
        <TextField
          label="Search name"
          value={table.search}
          onChange={(e) => table.setSearch(e.target.value)}
          size="small"
          sx={{ minWidth: 260 }}
        />
        <FormControlLabel
          label="Include inactive"
          control={
            <Switch
              checked={includeInactive}
              onChange={(e) => {
                setIncludeInactive(e.target.checked)
                table.resetPage()
              }}
            />
          }
        />
      </Stack>

      <ServerDataGrid query={query} columns={columns} gridProps={table.gridProps} noun="clubs" emptyText="No clubs match" />

      {creating && <ClubFormDialog onClose={() => setCreating(false)} />}
      {editing && <ClubFormDialog club={editing} onClose={() => setEditing(null)} />}
    </>
  )
}
