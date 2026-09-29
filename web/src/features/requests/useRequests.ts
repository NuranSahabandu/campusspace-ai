import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { type ListParams, usePagedQuery } from '../../api/list'
import type { BookingRequestDetailDto, BookingRequestSummaryDto, ClubDto, PagedResult } from '../../api/types'
import { clubsKeys } from '../clubs/useClubs'
import { refreshIntervalFor } from './requestStatus'

/** GET /api/booking-requests parameters. status is sent as repeated ?status=; from/to are inclusive campus dates. */
export interface BookingRequestsParams extends ListParams {
  status?: readonly string[]
  from?: string
  to?: string
  clubId?: number
}

export const bookingRequestsKeys = {
  all: ['booking-requests'] as const,
  list: (params: BookingRequestsParams) => [...bookingRequestsKeys.all, 'list', params] as const,
  detail: (id: number) => [...bookingRequestsKeys.all, 'detail', id] as const,
}

/** Grid column field → the server sort field (BookingRequestsQuery.SortFields). Budget, purpose and people are not sortable. */
export const BOOKING_REQUESTS_SORT_FIELDS: Record<string, string> = {
  when: 'requestedStart',
  attendees: 'attendees',
  status: 'status',
  createdAt: 'createdAt',
}

export function useBookingRequests(params: BookingRequestsParams, options: { enabled?: boolean } = {}) {
  return usePagedQuery<BookingRequestSummaryDto>(
    bookingRequestsKeys.list(params),
    '/api/booking-requests',
    // An empty status list means every status: send no ?status= at all.
    { ...params, status: params.status?.length ? params.status : undefined },
    // A request the agent is still planning changes on the server; keep the page current until none is.
    { ...options, refetchInterval: (page) => refreshIntervalFor(page?.items.map((r) => r.status) ?? []) },
  )
}

/**
 * One request. It re-fetches while its status refreshes (refreshIntervalFor), or every `refreshMs` when the caller is
 * waiting for something the status doesn't show (an approval in progress).
 */
export function useBookingRequest(id: number, { refreshMs }: { refreshMs?: number | false } = {}) {
  return useQuery({
    queryKey: bookingRequestsKeys.detail(id),
    queryFn: async ({ signal }) => (await api.get<BookingRequestDetailDto>(`/api/booking-requests/${id}`, { signal })).data,
    enabled: Number.isInteger(id) && id > 0,
    refetchInterval: (query) => refreshMs || refreshIntervalFor(query.state.data ? [query.state.data.status] : []),
  })
}

/**
 * The active clubs, for the Club filter, ordered by name. One request with pageSize 100, the API's maximum
 * (PageQuery.MaxPageSize). Any signed-in user may read them; only active clubs come back for non-Admins.
 */
export function useClubOptions() {
  return useQuery({
    queryKey: [...clubsKeys.all, 'options'],
    queryFn: async ({ signal }) =>
      (
        await api.get<PagedResult<ClubDto>>('/api/clubs', {
          params: { sort: 'name', page: 1, pageSize: 100 },
          signal,
        })
      ).data.items,
  })
}
