import { MAIN_QUERY_META } from '../../api/forbidden'
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

/** `main`: the Users page's own list (a 403 opens the access-denied page); the member picker leaves it out. */
export function useUsers(params: UsersParams, { enabled, main }: { enabled?: boolean; main?: boolean } = {}) {
  return usePagedQuery<UserDto>(usersKeys.list(params), '/api/users', params, {
    enabled,
    meta: main ? MAIN_QUERY_META : undefined,
  })
}
