import { Alert, Box, Button, Paper, Skeleton, Stack, Typography } from '@mui/material'
import { DataGrid, type GridColDef, type GridValidRowModel } from '@mui/x-data-grid'
import type { UseQueryResult } from '@tanstack/react-query'
import { parseProblem } from '../api/problem'
import type { PagedResult } from '../api/types'
import type { ServerTableGridProps } from '../hooks/useServerTable'

interface Props<T extends GridValidRowModel> {
  query: UseQueryResult<PagedResult<T>>
  columns: GridColDef<T>[]
  gridProps: ServerTableGridProps
  /** Plural noun for the loading and error text ("users"). */
  noun: string
  emptyText: string
  getRowId?: (row: T) => string | number
  height?: number
}

/**
 * A server-mode DataGrid with the §12 loading (skeleton), empty and error-with-Retry states.
 * Pair it with useServerTable (state) and a usePagedQuery hook (data).
 */
export function ServerDataGrid<T extends GridValidRowModel>({
  query,
  columns,
  gridProps,
  noun,
  emptyText,
  getRowId,
  height = 600,
}: Props<T>) {
  const { data, isPending, isFetching, isError, error, refetch } = query

  if (isError) {
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={() => refetch()}>
            Retry
          </Button>
        }
      >
        Could not load {noun}: {parseProblem(error).title}
      </Alert>
    )
  }

  if (isPending) {
    return (
      <Box aria-label={`Loading ${noun}`}>
        {Array.from({ length: 6 }, (_, i) => (
          <Skeleton key={i} variant="rectangular" height={44} sx={{ mb: 0.5 }} />
        ))}
      </Box>
    )
  }

  const NoRows = () => (
    <Stack sx={{ height: '100%', alignItems: 'center', justifyContent: 'center' }}>
      <Typography color="text.secondary">{emptyText}</Typography>
    </Stack>
  )

  return (
    <Paper variant="outlined" sx={{ height, width: '100%' }}>
      <DataGrid
        rows={data.items}
        columns={columns}
        getRowId={getRowId}
        rowCount={data.total}
        loading={isFetching}
        paginationMode="server"
        sortingMode="server"
        {...gridProps}
        pageSizeOptions={[10, 20, 50, 100]}
        disableColumnFilter
        disableRowSelectionOnClick
        // Pages are at most 100 rows, so virtualization buys nothing; turning it off
        // also lets rows render in jsdom, which cannot measure the grid.
        disableVirtualization
        slots={{ noRowsOverlay: NoRows }}
      />
    </Paper>
  )
}
