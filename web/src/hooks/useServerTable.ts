import { useMemo, useState } from 'react'
import type { GridPaginationModel, GridSortModel } from '@mui/x-data-grid'
import { makeToSortParam } from '../ui/sortParam'
import { useDebouncedValue } from './useDebouncedValue'

export interface ServerTableOptions {
  /** Grid column field → the list's server sort field. */
  sortFields: Record<string, string>
  initialSort?: GridSortModel
  pageSize?: number
}

/**
 * State for a server-mode DataGrid (§9 lists): search (debounced), paging and sorting, mapped to the
 * API's ?search=&sort=&page=&pageSize=. A new search or sort starts again from the first page;
 * pages call resetPage() when their own filters change.
 */
export function useServerTable({ sortFields, initialSort = [], pageSize = 20 }: ServerTableOptions) {
  const [search, setSearchValue] = useState('')
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({ page: 0, pageSize })
  const [sortModel, setSortModel] = useState<GridSortModel>(initialSort)
  const debouncedSearch = useDebouncedValue(search.trim(), 300)
  const toSortParam = useMemo(() => makeToSortParam(sortFields), [sortFields])

  const resetPage = () => setPaginationModel((p) => ({ ...p, page: 0 }))

  return {
    search,
    setSearch: (value: string) => {
      setSearchValue(value)
      resetPage()
    },
    resetPage,
    params: {
      search: debouncedSearch,
      sort: toSortParam(sortModel),
      page: paginationModel.page + 1, // the grid is 0-based, the API 1-based
      pageSize: paginationModel.pageSize,
    },
    gridProps: {
      paginationModel,
      onPaginationModelChange: setPaginationModel,
      sortModel,
      onSortModelChange: (model: GridSortModel) => {
        setSortModel(model)
        resetPage()
      },
    },
  }
}

export type ServerTableGridProps = ReturnType<typeof useServerTable>['gridProps']
