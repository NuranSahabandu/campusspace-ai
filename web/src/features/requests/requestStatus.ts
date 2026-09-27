/**
 * Booking request statuses, labels, chip colours and filter groups: the only list on the web. Mirrors
 * backend/CampusSpace.Api/Models/RequestStatuses.cs and uses the same labels and groups as the mobile app
 * (mobile/lib/features/requests/request_status.dart). Keep the three in sync.
 */
export const RequestStatuses = {
  Submitted: 'Submitted',
  AgentProcessing: 'AgentProcessing',
  PendingApproval: 'PendingApproval',
  Approved: 'Approved',
  Completed: 'Completed',
  AgentFailed: 'AgentFailed',
  RevisionRequested: 'RevisionRequested',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
} as const

export type RequestStatus = (typeof RequestStatuses)[keyof typeof RequestStatuses]

export const REQUEST_STATUSES: readonly RequestStatus[] = Object.values(RequestStatuses)

export const isRequestStatus = (value: string): value is RequestStatus =>
  (REQUEST_STATUSES as readonly string[]).includes(value)

const LABELS: Record<RequestStatus, string> = {
  Submitted: 'Submitted',
  AgentProcessing: 'Processing',
  PendingApproval: 'Waiting for approval',
  Approved: 'Approved',
  Completed: 'Completed',
  RevisionRequested: 'Needs revision',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
  AgentFailed: 'Failed',
}

/** The friendly label for a status; an unknown status is shown as sent. */
export const requestStatusLabel = (status: string): string => (isRequestStatus(status) ? LABELS[status] : status)

export type ChipColor = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error'
export interface ChipStyle {
  color: ChipColor
  variant: 'filled' | 'outlined'
}

// MUI has fewer chip colours than there are statuses, so related statuses share a colour and differ in variant.
const STYLES: Record<RequestStatus, ChipStyle> = {
  Submitted: { color: 'info', variant: 'filled' },
  AgentProcessing: { color: 'primary', variant: 'outlined' },
  PendingApproval: { color: 'warning', variant: 'filled' },
  Approved: { color: 'success', variant: 'filled' },
  Completed: { color: 'success', variant: 'outlined' },
  RevisionRequested: { color: 'warning', variant: 'outlined' },
  Rejected: { color: 'error', variant: 'filled' },
  Cancelled: { color: 'default', variant: 'outlined' },
  AgentFailed: { color: 'error', variant: 'outlined' },
}

export const requestStatusChipStyle = (status: string): ChipStyle =>
  isRequestStatus(status) ? STYLES[status] : { color: 'default', variant: 'outlined' }

/** The list's quick filter chips, each sent as repeated ?status= values. An empty list means every status. */
export const STATUS_GROUPS = {
  all: { label: 'All', statuses: [] },
  open: {
    label: 'Open',
    statuses: [
      RequestStatuses.Submitted,
      RequestStatuses.AgentProcessing,
      RequestStatuses.PendingApproval,
      RequestStatuses.RevisionRequested,
    ],
  },
  approved: { label: 'Approved', statuses: [RequestStatuses.Approved, RequestStatuses.Completed] },
  closed: {
    label: 'Closed',
    statuses: [RequestStatuses.Rejected, RequestStatuses.Cancelled, RequestStatuses.AgentFailed],
  },
} as const satisfies Record<string, { label: string; statuses: readonly RequestStatus[] }>

export type StatusGroup = keyof typeof STATUS_GROUPS

export const STATUS_GROUP_KEYS = Object.keys(STATUS_GROUPS) as StatusGroup[]
export const DEFAULT_STATUS_GROUP: StatusGroup = 'open'

export const isStatusGroup = (value: string): value is StatusGroup => Object.hasOwn(STATUS_GROUPS, value)
