import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { MAIN_QUERY_META } from '../../api/forbidden'
import { type ListParams, usePagedQuery } from '../../api/list'
import type { BlackoutClashDto, BlackoutDto, BuildingDto, FeatureDto, RoomDto } from '../../api/types'

/** GET /api/rooms filters. features is a comma-separated list of codes (a room must have all of them). */
export interface RoomsParams extends ListParams {
  buildingId?: number
  type?: string
  minCapacity?: number
  features?: string
  includeInactive?: boolean
}

/** GET /api/rooms/{id}/blackouts. Returns blackouts overlapping [from, to); a missing bound is unbounded. */
export interface BlackoutsParams extends ListParams {
  from?: string
  to?: string
}

// Room writes invalidate roomsKeys.all (list, detail, blackouts and their clashes; a request cancellation does too).
// Building and feature writes also invalidate it, because rooms embed building and feature names.
export const roomsKeys = {
  all: ['rooms'] as const,
  list: (params: RoomsParams) => [...roomsKeys.all, 'list', params] as const,
  detail: (id: number) => [...roomsKeys.all, 'detail', id] as const,
  blackouts: (roomId: number, params: BlackoutsParams) => [...roomsKeys.all, 'blackouts', roomId, params] as const,
  clashes: (roomId: number, blackoutId: number) => [...roomsKeys.all, 'clashes', roomId, blackoutId] as const,
}
export const buildingsKeys = { all: ['buildings'] as const }
export const featuresKeys = { all: ['features'] as const }

export const ROOMS_SORT_FIELDS: Record<string, string> = {
  code: 'code',
  name: 'name',
  capacity: 'capacity',
  building: 'building',
}
export const BLACKOUTS_SORT_FIELDS: Record<string, string> = { start: 'start' }

export function useRooms(params: RoomsParams) {
  return usePagedQuery<RoomDto>(roomsKeys.list(params), '/api/rooms', params, { meta: MAIN_QUERY_META })
}

export function useRoom(id: number) {
  return useQuery({
    queryKey: roomsKeys.detail(id),
    queryFn: async ({ signal }) => (await api.get<RoomDto>(`/api/rooms/${id}`, { signal })).data,
    // A non-numeric URL (/rooms/abc) is "not found" without a request.
    enabled: Number.isInteger(id) && id > 0,
    meta: MAIN_QUERY_META,
  })
}

export function useBlackouts(roomId: number, params: BlackoutsParams) {
  return usePagedQuery<BlackoutDto>(roomsKeys.blackouts(roomId, params), `/api/rooms/${roomId}/blackouts`, params)
}

/**
 * GET /api/rooms/{id}/blackouts/{blackoutId}/clashes: the active bookings the blackout clashes with now. initialData
 * seeds it from the create response, so the add-blackout warning shows at once and still refreshes after a cancel.
 */
export function useBlackoutClashes(roomId: number, blackoutId: number, initialData?: BlackoutClashDto[]) {
  return useQuery({
    queryKey: roomsKeys.clashes(roomId, blackoutId),
    queryFn: async ({ signal }) =>
      (await api.get<BlackoutClashDto[]>(`/api/rooms/${roomId}/blackouts/${blackoutId}/clashes`, { signal })).data,
    initialData,
  })
}

/** Small, unpaged reference lists. */
export function useBuildings() {
  return useQuery({
    queryKey: buildingsKeys.all,
    queryFn: async ({ signal }) => (await api.get<BuildingDto[]>('/api/buildings', { signal })).data,
  })
}

export function useFeatures() {
  return useQuery({
    queryKey: featuresKeys.all,
    queryFn: async ({ signal }) => (await api.get<FeatureDto[]>('/api/features', { signal })).data,
  })
}

/** Toast for a delete that the database blocks (409 "In use"). */
export const IN_USE_MESSAGE = "Can't delete: still used by rooms."
