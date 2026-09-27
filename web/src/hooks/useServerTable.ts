import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import type { GridPaginationModel, GridSortModel } from '@mui/x-data-grid'
import { makeToSortParam } from '../ui/sortParam'
import { useDebouncedValue } from './useDebouncedValue'

/** The page sizes ServerDataGrid offers. */
export const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const

export interface ServerTableOptions {
  /** Grid column field → the list's server sort field. */
  sortFields: Record<string, string>
  initialSort?: GridSortModel
  pageSize?: number
  /**
   * Keep search, sort, page and pageSize in the URL query (?search=&sort=&page=&pageSize=, the API's own names and
   * values) instead of component state, so going back to the list restores them. Default values are left out.
   */
  urlState?: boolean
}

/** Changes the URL query in one navigation. The table's own keys and the page's filter keys share it. */
export type UpdateUrl = (mutate: (params: URLSearchParams) => void) => void

/**
 * State for a server-mode DataGrid (§9 lists): search (debounced), paging and sorting, mapped to the
 * API's ?search=&sort=&page=&pageSize=. A new search or sort starts again from the first page;
 * pages call resetPage() when their own filters change (or updateUrl(), which also does, in URL mode).
 */
export function useServerTable({ sortFields, initialSort = [], pageSize = 20, urlState = false }: ServerTableOptions) {
  const [searchState, setSearchState] = useState('')
  const [paginationState, setPaginationState] = useState<GridPaginationModel>({ page: 0, pageSize })
  const [sortState, setSortState] = useState<GridSortModel>(initialSort)
  const [searchParams, setSearchParams] = useSearchParams()
  const toSortParam = useMemo(() => makeToSortParam(sortFields), [sortFields])

  const search = urlState ? (searchParams.get('search') ?? '') : searchState
  // Memoized on the raw values so the grid gets the same model objects until the URL really changes.
  const rawPage = searchParams.get('page')
  const rawPageSize = searchParams.get('pageSize')
  const rawSort = searchParams.get('sort')
  const urlPagination = useMemo(() => paginationFromUrl(rawPage, rawPageSize, pageSize), [rawPage, rawPageSize, pageSize])
  // initialSort is usually an inline literal, so key it by value.
  const initialSortKey = JSON.stringify(initialSort)
  const urlSort = useMemo(
    () => sortFromUrl(rawSort, sortFields, JSON.parse(initialSortKey) as GridSortModel),
    [rawSort, sortFields, initialSortKey],
  )
  const paginationModel = urlState ? urlPagination : paginationState
  const sortModel = urlState ? urlSort : sortState
  const debouncedSearch = useDebouncedValue(search.trim(), 300)

  /** In URL mode: apply `mutate` and go back to the first page, in one replace navigation. */
  const updateUrl: UpdateUrl = (mutate) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        mutate(next)
        next.delete('page')
        return next
      },
      { replace: true },
    )

  const resetPage = () => {
    if (urlState) updateUrl(() => {})
    else setPaginationState((p) => ({ ...p, page: 0 }))
  }

  const setSearch = (value: string) => {
    if (urlState)
      updateUrl((p) => {
        if (value) p.set('search', value)
        else p.delete('search')
      })
    else {
      setSearchState(value)
      resetPage()
    }
  }

  const onPaginationModelChange = (model: GridPaginationModel) => {
    if (!urlState) return setPaginationState(model)
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (model.page > 0) next.set('page', String(model.page + 1))
        else next.delete('page')
        if (model.pageSize !== pageSize) next.set('pageSize', String(model.pageSize))
        else next.delete('pageSize')
        return next
      },
      { replace: true },
    )
  }

  const onSortModelChange = (model: GridSortModel) => {
    if (!urlState) {
      setSortState(model)
      resetPage()
      return
    }
    updateUrl((p) => {
      const value = toSortParam(model)
      // Absent means the initial sort; present but empty means "no sort" (the server's default order).
      if (sameSort(model, initialSort)) p.delete('sort')
      else p.set('sort', value ?? '')
    })
  }

  return {
    search,
    setSearch,
    resetPage,
    updateUrl,
    params: {
      search: debouncedSearch,
      sort: toSortParam(sortModel),
      page: paginationModel.page + 1, // the grid is 0-based, the API 1-based
      pageSize: paginationModel.pageSize,
    },
    gridProps: {
      paginationModel,
      onPaginationModelChange,
      sortModel,
      onSortModelChange,
    },
  }
}

export type ServerTableGridProps = ReturnType<typeof useServerTable>['gridProps']

const sameSort = (a: GridSortModel, b: GridSortModel) =>
  a.length === b.length && a.every((item, i) => item.field === b[i].field && item.sort === b[i].sort)

/** ?page= (1-based) and ?pageSize= from the URL; values that are not valid fall back to the defaults. */
function paginationFromUrl(rawPage: string | null, rawPageSize: string | null, defaultPageSize: number): GridPaginationModel {
  const page = Number(rawPage)
  const size = Number(rawPageSize)
  return {
    page: Number.isInteger(page) && page >= 1 ? page - 1 : 0,
    pageSize: (PAGE_SIZE_OPTIONS as readonly number[]).includes(size) ? size : defaultPageSize,
  }
}

/** ?sort= (the server's "-field" form) back to a grid sort model; an unknown field falls back to the initial sort. */
function sortFromUrl(raw: string | null, sortFields: Record<string, string>, initialSort: GridSortModel): GridSortModel {
  if (raw === null) return initialSort
  if (raw === '') return []
  const desc = raw.startsWith('-')
  const serverField = desc ? raw.slice(1) : raw
  const field = Object.keys(sortFields).find((f) => sortFields[f] === serverField)
  return field ? [{ field, sort: desc ? 'desc' : 'asc' }] : initialSort
}
