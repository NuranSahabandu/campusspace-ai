import { keepPreviousData, useQuery, type QueryKey } from '@tanstack/react-query'
import { api } from './client'
import type { PagedResult } from './types'

/** §9 list query parameters every list shares. page is 1-based. */
export interface ListParams {
  search?: string
  sort?: string
  page: number
  pageSize: number
}

// Drop empty values so the URL only carries what is set.
export const compactParams = (params: object) =>
  Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined && v !== '' && v !== null))

/** GET a §9 list endpoint. Keeps showing the current page while the next one loads. */
export function usePagedQuery<T>(queryKey: QueryKey, url: string, params: object, options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const { data } = await api.get<PagedResult<T>>(url, { params: compactParams(params), signal })
      return data
    },
    placeholderData: keepPreviousData,
    enabled: options.enabled,
  })
}
