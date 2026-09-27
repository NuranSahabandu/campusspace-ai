import { Roles } from '../../auth/roles'
import { formatLkr } from '../../ui/formatLkr'

// Room types have one list on the client (roomTypes.ts, mirroring Models/RoomTypes.cs); pricing reuses it.
export { ROOM_TYPES, roomTypeLabel } from '../facilities/roomTypes'

// Mirrors RequesterRoles and PricingRuleStatuses in backend/CampusSpace.Api/Models. Keep them in sync.
export const REQUESTER_ROLES = [Roles.Student, Roles.Lecturer] as const
export const PRICING_STATUSES = { Current: 'Current', Scheduled: 'Scheduled', Superseded: 'Superseded' } as const

type ChipColor = 'default' | 'success' | 'info'
const STATUS_COLORS: Record<string, ChipColor> = { Current: 'success', Scheduled: 'info', Superseded: 'default' }
export const pricingStatusColor = (status: string): ChipColor => STATUS_COLORS[status] ?? 'default'

/** numeric(10,2) and the Range on PricingRuleRequest.HourlyRate. */
export const MAX_RATE = 99_999_999.99

export const PRICING_EXPLAINER =
  'Prices are history. To change a price, add a rule that starts on a later date; the current rule stays for bookings before that date.'
export const IN_EFFECT_TOOLTIP = "Rules already in effect can't be changed. Add a new rule with a later start date."

/** "LKR 1,500.00 / h", or "Exempt" for a free rule. */
export const formatRate = ({ hourlyRate, isExempt }: { hourlyRate: number; isExempt: boolean }) =>
  isExempt ? 'Exempt' : `${formatLkr(hourlyRate)} / h`
