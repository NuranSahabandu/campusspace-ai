import type { GridSortModel } from '@mui/x-data-grid'

// Grid column → server sort field (UsersQuery.SortFields in the API).
const SORT_FIELDS: Record<string, string> = {
  fullName: 'name',
  email: 'email',
  role: 'role',
  createdAt: 'createdAt',
}

/** Grid sort model → the API's ?sort= value ("name", "-createdAt", ...). */
export const toSortParam = (model: GridSortModel): string | undefined => {
  const [first] = model
  if (!first?.sort) return undefined
  const field = SORT_FIELDS[first.field]
  return first.sort === 'desc' ? `-${field}` : field
}
