import { Box, Paper, Skeleton, Stack, Typography } from '@mui/material'
import { DataGrid, type GridColDef, type GridRowParams, type GridValidRowModel } from '@mui/x-data-grid'
import type { UseQueryResult } from '@tanstack/react-query'
import type { PagedResult } from '../api/types'
import { PAGE_SIZE_OPTIONS, type ServerTableGridProps } from '../hooks/useServerTable'
import { QueryErrorAlert } from './QueryErrorAlert'

interface Props<T extends GridValidRowModel> {
  query: UseQueryResult<PagedResult<T>>
  columns: GridColDef<T>[]
  gridProps: ServerTableGridProps
  /** Plural noun for the loading and error text ("users"). */
  noun: string
  emptyText: string
  getRowId?: (row: T) => string | number
  height?: number
  /** Opens a row (for example its detail page). Rows then show a pointer cursor. */
  onRowClick?: (row: T) => void
  /** Fixed row height, for cells with two lines. */
  rowHeight?: number
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
  onRowClick,
  rowHeight,
}: Props<T>) {
  const { data, isPending, isFetching, isError, error, refetch, fetchStatus } = query

  if (isError) {
    return (
      <QueryErrorAlert error={error} what={noun} onRetry={() => refetch()} />
    )
  }

  // A disabled query that has never loaded (for example while a filter is invalid) is not loading: show an empty grid.
  const neverLoaded = isPending && fetchStatus === 'idle'

  if (isPending && !neverLoaded) {
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
        rows={data?.items ?? []}
        columns={columns}
        getRowId={getRowId}
        rowCount={data?.total ?? 0}
        loading={isFetching}
        paginationMode="server"
        sortingMode="server"
        {...gridProps}
        pageSizeOptions={[...PAGE_SIZE_OPTIONS]}
        rowHeight={rowHeight}
        onRowClick={onRowClick ? (params: GridRowParams<T>) => onRowClick(params.row) : undefined}
        sx={onRowClick ? { '& .MuiDataGrid-row': { cursor: 'pointer' } } : undefined}
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
