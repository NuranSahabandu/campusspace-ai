import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { PagedResult, UserDto } from '../../api/types'

/** GET /api/users query parameters (§9 list conventions plus a role filter). page is 1-based. */
export interface UsersParams {
  search?: string
  role?: string
  sort?: string
  page: number
  pageSize: number
}

export const usersKeys = {
  all: ['users'] as const,
  list: (params: UsersParams) => [...usersKeys.all, 'list', params] as const,
}

export function useUsers(params: UsersParams) {
  return useQuery({
    queryKey: usersKeys.list(params),
    queryFn: async ({ signal }) => {
      // Drop empty values so the URL only carries what is set.
      const query = Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined && v !== ''))
      const { data } = await api.get<PagedResult<UserDto>>('/api/users', { params: query, signal })
      return data
    },
    // Keep showing the current page while the next one loads.
    placeholderData: keepPreviousData,
  })
}
