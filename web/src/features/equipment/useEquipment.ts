import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { MAIN_QUERY_META } from '../../api/forbidden'
import { type ListParams, usePagedQuery } from '../../api/list'
import type { EquipmentItemDto, EquipmentTypeDto, EquipmentTypeRefDto, PagedResult } from '../../api/types'

export interface EquipmentTypesParams extends ListParams {
  category?: string
}

export interface EquipmentItemsParams extends ListParams {
  typeId?: number
  status?: string
  condition?: string
}

// Type writes invalidate equipmentTypesKeys.all (list, options, substitutes). Items embed the type's code and name and
// types embed item counts, so type edits and item writes invalidate both roots.
export const equipmentTypesKeys = {
  all: ['equipment-types'] as const,
  list: (params: EquipmentTypesParams) => [...equipmentTypesKeys.all, 'list', params] as const,
  options: () => [...equipmentTypesKeys.all, 'options'] as const,
  categories: () => [...equipmentTypesKeys.all, 'categories'] as const,
  substitutes: (id: number) => [...equipmentTypesKeys.all, 'substitutes', id] as const,
}
export const equipmentItemsKeys = {
  all: ['equipment-items'] as const,
  list: (params: EquipmentItemsParams) => [...equipmentItemsKeys.all, 'list', params] as const,
}

export const EQUIPMENT_TYPES_SORT_FIELDS: Record<string, string> = {
  code: 'code',
  name: 'name',
  category: 'category',
  feePerBooking: 'fee',
}
export const EQUIPMENT_ITEMS_SORT_FIELDS: Record<string, string> = {
  assetTag: 'assetTag',
  type: 'type',
  status: 'status',
  condition: 'condition',
  updatedAt: 'updatedAt',
}

export function useEquipmentTypes(params: EquipmentTypesParams) {
  return usePagedQuery<EquipmentTypeDto>(equipmentTypesKeys.list(params), '/api/equipment-types', params, {
    meta: MAIN_QUERY_META,
  })
}

export function useEquipmentItems(params: EquipmentItemsParams) {
  return usePagedQuery<EquipmentItemDto>(equipmentItemsKeys.list(params), '/api/equipment-items', params, {
    meta: MAIN_QUERY_META,
  })
}

/** The fixed category list (EquipmentCategories on the server). */
export function useEquipmentCategories() {
  return useQuery({
    queryKey: equipmentTypesKeys.categories(),
    queryFn: async ({ signal }) => (await api.get<string[]>('/api/equipment-types/categories', { signal })).data,
    staleTime: Infinity,
  })
}

/**
 * Every type, for selects and filters, ordered by code. One request with pageSize 100, the API's maximum
 * (PageQuery.MaxPageSize); the seeded catalogue is far smaller. If it ever passes 100 types, switch the pickers to a
 * server-search Autocomplete rather than fetching page after page.
 */
export function useEquipmentTypeOptions() {
  return useQuery({
    queryKey: equipmentTypesKeys.options(),
    queryFn: async ({ signal }) =>
      (
        await api.get<PagedResult<EquipmentTypeDto>>('/api/equipment-types', {
          params: { sort: 'code', page: 1, pageSize: 100 },
          signal,
        })
      ).data.items,
  })
}

export function useSubstitutes(typeId: number) {
  return useQuery({
    queryKey: equipmentTypesKeys.substitutes(typeId),
    queryFn: async ({ signal }) =>
      (await api.get<EquipmentTypeRefDto[]>(`/api/equipment-types/${typeId}/substitutes`, { signal })).data,
  })
}
