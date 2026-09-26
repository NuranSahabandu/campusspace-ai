import { makeToSortParam } from '../../ui/sortParam'

// Grid column → server sort field (UsersQuery.SortFields in the API).
export const USERS_SORT_FIELDS: Record<string, string> = {
  fullName: 'name',
  email: 'email',
  role: 'role',
  createdAt: 'createdAt',
}

/** Grid sort model → the API's ?sort= value ("name", "-createdAt", ...). */
export const toSortParam = makeToSortParam(USERS_SORT_FIELDS)
