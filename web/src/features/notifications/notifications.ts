import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { NotificationLogDto } from '../../api/types'
import { bookingRequestsKeys } from '../requests/useRequests'

/** Mirrors NotificationStatuses (backend). Pending and Sending are still on their way. */
export const NOTIFICATION_STATUS_COLORS: Record<string, 'default' | 'primary' | 'success' | 'warning' | 'error'> = {
  Pending: 'primary',
  Sending: 'primary',
  Sent: 'success',
  Failed: 'error',
  Skipped: 'default',
}

/** Mirrors NotificationKinds (backend): what each email tells the requester. */
export const NOTIFICATION_KIND_LABELS: Record<string, string> = {
  Approved: 'Booking confirmed (with calendar file)',
  Rejected: 'Not approved',
  Closed: 'Closed automatically',
  RevisionRequested: 'Being re-planned',
  CancelledByOfficer: 'Cancelled by Facilities',
}

/** How often the list re-fetches while an email is Pending or Sending (the dispatcher ticks every 5 s). */
export const NOTIFICATIONS_REFRESH_MS = 5_000

export const isInFlight = (items: NotificationLogDto[] | undefined) =>
  items?.some((n) => n.status === 'Pending' || n.status === 'Sending') ?? false

export const notificationsKeys = {
  forRequest: (requestId: number, version: string) => [...bookingRequestsKeys.all, 'notifications', requestId, version] as const,
}

/** The request's emails, newest first (Facilities Officer). Keyed by the request's updatedAt, like its runs. */
export function useRequestNotifications(requestId: number, version: string | undefined) {
  return useQuery({
    queryKey: notificationsKeys.forRequest(requestId, version ?? ''),
    queryFn: async ({ signal }) =>
      (await api.get<NotificationLogDto[]>(`/api/booking-requests/${requestId}/notifications`, { signal })).data,
    enabled: version !== undefined,
    placeholderData: keepPreviousData,
    refetchInterval: (query) => (isInFlight(query.state.data) ? NOTIFICATIONS_REFRESH_MS : false),
  })
}
