import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControl,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Skeleton,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { DataGrid, type GridColDef, type GridPaginationModel, type GridSortModel } from '@mui/x-data-grid'
import { parseProblem } from '../../api/problem'
import type { UserDto } from '../../api/types'
import { ALL_ROLES } from '../../auth/roles'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'
import { useUsers } from './useUsers'
import { toSortParam } from './usersSort'

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

function NoUsers() {
  return (
    <Stack sx={{ height: '100%', alignItems: 'center', justifyContent: 'center' }}>
      <Typography color="text.secondary">No users match</Typography>
    </Stack>
  )
}

/**
 * Reference data view for every team: a server-mode DataGrid fed by TanStack Query, with search, filter,
 * sort and paging mapped to the API's list parameters, plus loading, empty and error states (§12).
 */
export function UsersPage() {
  const [search, setSearch] = useState('')
  const [role, setRole] = useState('')
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 20 })
  const [sortModel, setSortModel] = useState<GridSortModel>([])
  const debouncedSearch = useDebouncedValue(search.trim(), 300)

  const { data, isPending, isFetching, isError, error, refetch } = useUsers({
    search: debouncedSearch,
    role,
    sort: toSortParam(sortModel),
    page: pagination.page + 1, // the grid is 0-based, the API 1-based
    pageSize: pagination.pageSize,
  })

  // A new search or filter starts again from the first page.
  const resetPage = () => setPagination((p) => ({ ...p, page: 0 }))

  return (
    <>
      <Typography variant="h4" component="h1" gutterBottom>
        Users
      </Typography>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 2 }}>
        <TextField
          label="Search name or email"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value)
            resetPage()
          }}
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
              resetPage()
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

      {isError ? (
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => refetch()}>
              Retry
            </Button>
          }
        >
          Could not load users: {parseProblem(error).title}
        </Alert>
      ) : isPending ? (
        <Box aria-label="Loading users">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} variant="rectangular" height={44} sx={{ mb: 0.5 }} />
          ))}
        </Box>
      ) : (
        <Paper variant="outlined" sx={{ height: 600, width: '100%' }}>
          <DataGrid
            rows={data.items}
            columns={columns}
            rowCount={data.total}
            loading={isFetching}
            paginationMode="server"
            sortingMode="server"
            paginationModel={pagination}
            onPaginationModelChange={setPagination}
            sortModel={sortModel}
            onSortModelChange={(model) => {
              setSortModel(model)
              resetPage()
            }}
            pageSizeOptions={[10, 20, 50, 100]}
            disableColumnFilter
            disableRowSelectionOnClick
            // Pages are at most 100 rows, so virtualization buys nothing; turning it off
            // also lets rows render in jsdom, which cannot measure the grid.
            disableVirtualization
            slots={{ noRowsOverlay: NoUsers }}
          />
        </Paper>
      )}
    </>
  )
}
