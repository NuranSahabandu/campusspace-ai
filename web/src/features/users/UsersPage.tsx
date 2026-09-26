import { useState } from 'react'
import { Chip, FormControl, InputLabel, MenuItem, Select, Stack, TextField, Typography } from '@mui/material'
import type { GridColDef } from '@mui/x-data-grid'
import type { UserDto } from '../../api/types'
import { ALL_ROLES } from '../../auth/roles'
import { useServerTable } from '../../hooks/useServerTable'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { useUsers } from './useUsers'
import { USERS_SORT_FIELDS } from './usersSort'

const columns: GridColDef<UserDto>[] = [
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
    valueFormatter: (value: string) => new Date(value).toLocaleString(),
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

  return (
    <>
      <Typography variant="h4" component="h1" gutterBottom>
        Users
      </Typography>

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
    </>
  )
}
