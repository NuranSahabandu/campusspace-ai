import { type ListParams, usePagedQuery } from '../../api/list'
import type { UserDto } from '../../api/types'

/** GET /api/users query parameters (§9 list conventions plus a role filter). */
export interface UsersParams extends ListParams {
  role?: string
}

export const usersKeys = {
  all: ['users'] as const,
  list: (params: UsersParams) => [...usersKeys.all, 'list', params] as const,
}

export function useUsers(params: UsersParams, options: { enabled?: boolean } = {}) {
  return usePagedQuery<UserDto>(usersKeys.list(params), '/api/users', params, options)
}
