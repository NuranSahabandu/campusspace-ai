import { useState } from 'react'
import AddIcon from '@mui/icons-material/Add'
import EditIcon from '@mui/icons-material/Edit'
import { Button, Chip, FormControl, InputLabel, MenuItem, Select, Stack, TextField, Typography } from '@mui/material'
import { GridActionsCellItem, type GridColDef } from '@mui/x-data-grid'
import type { UserDto } from '../../api/types'
import { useAuthStore } from '../../auth/authStore'
import { ALL_ROLES } from '../../auth/roles'
import { useServerTable } from '../../hooks/useServerTable'
import { formatDateTime } from '../../ui/formatDateTime'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { CreateUserDialog, EditUserDialog } from './UserFormDialogs'
import { useUsers } from './useUsers'
import { USERS_SORT_FIELDS } from './usersSort'

const baseColumns: GridColDef<UserDto>[] = [
  { field: 'fullName', headerName: 'Name', flex: 1, minWidth: 160 },
  { field: 'email', headerName: 'Email', flex: 1, minWidth: 220 },
  { field: 'role', headerName: 'Role', width: 160 },
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
    field: 'createdAt',
    headerName: 'Created',
    width: 180,
    valueFormatter: (value: string) => formatDateTime(value),
  },
]

/**
 * Reference data view for every team: a server-mode DataGrid fed by TanStack Query, with search, filter,
 * sort and paging mapped to the API's list parameters, plus loading, empty and error states (§12).
 */
export function UsersPage() {
  const table = useServerTable({ sortFields: USERS_SORT_FIELDS })
  const [role, setRole] = useState('')
  const query = useUsers({ ...table.params, role })
  const currentUserId = useAuthStore((s) => s.user?.id)
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<UserDto | null>(null)

  const columns: GridColDef<UserDto>[] = [
    ...baseColumns,
    {
      field: 'actions',
      type: 'actions',
      headerName: 'Actions',
      width: 90,
      getActions: ({ row }) => [
        <GridActionsCellItem key="edit" icon={<EditIcon />} label={`Edit ${row.fullName}`} onClick={() => setEditing(row)} />,
      ],
    },
  ]

  return (
    <>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h4" component="h1">
          Users
        </Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
          New user
        </Button>
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 2 }}>
        <TextField
          label="Search name or email"
          value={table.search}
          onChange={(e) => table.setSearch(e.target.value)}
          size="small"
          sx={{ minWidth: 260 }}
        />
        <FormControl size="small" sx={{ minWidth: 200 }}>
          <InputLabel id="role-filter-label">Role</InputLabel>
          <Select
            labelId="role-filter-label"
            label="Role"
            value={role}
            onChange={(e) => {
              setRole(e.target.value)
              table.resetPage()
            }}
          >
            <MenuItem value="">All roles</MenuItem>
            {ALL_ROLES.map((r) => (
              <MenuItem key={r} value={r}>
                {r}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Stack>

      <ServerDataGrid query={query} columns={columns} gridProps={table.gridProps} noun="users" emptyText="No users match" />

      {creating && <CreateUserDialog onClose={() => setCreating(false)} />}
      {editing && (
        <EditUserDialog user={editing} isSelf={editing.id === currentUserId} onClose={() => setEditing(null)} />
      )}
    </>
  )
}
