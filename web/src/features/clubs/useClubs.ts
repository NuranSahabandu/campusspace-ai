import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { MAIN_QUERY_META } from '../../api/forbidden'
import { type ListParams, usePagedQuery } from '../../api/list'
import type { ClubDetailDto, ClubDto } from '../../api/types'

/** GET /api/clubs parameters. includeInactive is honoured for Admins only. */
export interface ClubsParams extends ListParams {
  includeInactive?: boolean
}

// Every club write invalidates clubsKeys.all, which refreshes both the list and the open detail.
export const clubsKeys = {
  all: ['clubs'] as const,
  list: (params: ClubsParams) => [...clubsKeys.all, 'list', params] as const,
  detail: (id: number) => [...clubsKeys.all, 'detail', id] as const,
}

export const CLUBS_SORT_FIELDS: Record<string, string> = { name: 'name' }

export function useClubs(params: ClubsParams) {
  return usePagedQuery<ClubDto>(clubsKeys.list(params), '/api/clubs', params, { meta: MAIN_QUERY_META })
}

export function useClub(id: number) {
  return useQuery({
    queryKey: clubsKeys.detail(id),
    queryFn: async ({ signal }) => (await api.get<ClubDetailDto>(`/api/clubs/${id}`, { signal })).data,
    // A non-numeric URL (/clubs/abc) is "not found" without a request.
    enabled: Number.isInteger(id) && id > 0,
    meta: MAIN_QUERY_META,
  })
}
