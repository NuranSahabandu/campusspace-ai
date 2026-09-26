import type { GridSortModel } from '@mui/x-data-grid'

/**
 * Builds a grid sort model → API ?sort= mapper ("name", "-createdAt", ...).
 * `fields` maps grid column fields to the list's server sort fields; unmapped columns send no sort.
 */
export const makeToSortParam =
  (fields: Record<string, string>) =>
  (model: GridSortModel): string | undefined => {
    const [first] = model
    if (!first?.sort) return undefined
    const field = fields[first.field]
    if (!field) return undefined
    return first.sort === 'desc' ? `-${field}` : field
  }
