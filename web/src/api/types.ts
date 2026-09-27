/** Mirrors the backend DTOs. Dates are ISO-8601 UTC strings. */
export interface UserDto {
  id: number
  fullName: string
  email: string
  role: string
  isActive: boolean
  createdAt: string
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: UserDto
}

/** §9 list response. */
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface CreateUserRequest {
  fullName: string
  email: string
  password: string
  role: string
}

export interface UpdateUserRequest {
  fullName: string
  role: string
  isActive: boolean
}

/** A club in a list. representativeName is null when the club has no representative. */
export interface ClubDto {
  id: number
  name: string
  isActive: boolean
  memberCount: number
  representativeName: string | null
}

export interface ClubMemberDto {
  userId: number
  fullName: string
  role: string
  isRepresentative: boolean
  joinedAt: string
}

/** A club with its members, representative first. */
export interface ClubDetailDto {
  id: number
  name: string
  isActive: boolean
  createdAt: string
  updatedAt: string
  members: ClubMemberDto[]
}

export interface CreateClubRequest {
  name: string
}

export interface UpdateClubRequest {
  name: string
  isActive: boolean
}

export interface AddClubMemberRequest {
  userId: number
  isRepresentative: boolean
}

/** One audit row. userName is null for anonymous events or a deleted user. details is the stored JSON object. */
export interface AuditLogDto {
  id: number
  userId: number | null
  userName: string | null
  action: string
  entityType: string
  entityId: string | null
  details: Record<string, unknown>
  at: string
}

/** A building. Officers also see inactive ones; everyone else sees active ones only. */
export interface BuildingDto {
  id: number
  code: string
  name: string
  isActive: boolean
}

export interface CreateBuildingRequest {
  code: string
  name: string
}

export interface UpdateBuildingRequest {
  code: string
  name: string
  isActive: boolean
}

/** A room feature. The code (snake_case) is what the agents use. */
export interface FeatureDto {
  id: number
  code: string
  name: string
}

export interface FeatureRequest {
  code: string
  name: string
}

export interface BuildingRefDto {
  id: number
  code: string
  name: string
}

export interface FeatureRefDto {
  code: string
  name: string
}

/** A room with its building and features (features ordered by code). type is one of ROOM_TYPES. */
export interface RoomDto {
  id: number
  code: string
  name: string
  type: string
  capacity: number
  isActive: boolean
  building: BuildingRefDto
  features: FeatureRefDto[]
}

export interface CreateRoomRequest {
  code: string
  name: string
  type: string
  capacity: number
  buildingId: number
  featureCodes: string[]
}

/** Replaces the whole feature set. isActive = true reactivates a deactivated room. */
export interface UpdateRoomRequest extends CreateRoomRequest {
  isActive: boolean
}

/** A maintenance blackout as [start, end), both UTC. */
export interface BlackoutDto {
  id: number
  roomId: number
  start: string
  end: string
  reason: string
  createdById: number
  createdByName: string
  createdAt: string
}

/** An active booking of the blackout's room that overlaps it (UC14). Start/end are UTC. */
export interface BlackoutClashDto {
  bookingId: number
  requestId: number
  start: string
  end: string
  status: string
  requesterName: string
  requesterEmail: string
}

/** POST /api/rooms/{id}/blackouts: the blackout plus the bookings it clashes with (never cancelled automatically). */
export interface BlackoutWithClashesDto extends BlackoutDto {
  clashes: BlackoutClashDto[]
}

/** Times carry an offset (campusLocalToIso gives +05:30); the server stores UTC. */
export interface CreateBlackoutRequest {
  start: string
  end: string
  reason: string
}

/** How many items of an equipment type are in each status. */
export interface EquipmentItemCountsDto {
  total: number
  available: number
  onLoan: number
  underRepair: number
  retired: number
}

/** An equipment type in a list. coveredByFeatureName is null when coveredByFeatureCode is. */
export interface EquipmentTypeDto {
  id: number
  code: string
  name: string
  category: string
  feePerBooking: number
  coveredByFeatureCode: string | null
  coveredByFeatureName: string | null
  itemCounts: EquipmentItemCountsDto
}

export interface EquipmentTypeRefDto {
  id: number
  code: string
  name: string
}

/** The code cannot change on update. coveredByFeatureCode null means none. */
export interface EquipmentTypeRequest {
  code: string
  name: string
  category: string
  feePerBooking: number
  coveredByFeatureCode: string | null
}

/** An equipment item. condition is one of EQUIPMENT_CONDITIONS, status one of EQUIPMENT_STATUSES. */
export interface EquipmentItemDto {
  id: number
  assetTag: string
  typeId: number
  typeCode: string
  typeName: string
  condition: string
  status: string
  notes: string | null
  updatedAt: string
}

/** typeId cannot change on update; status OnLoan is set only by loans. */
export interface EquipmentItemRequest {
  assetTag: string
  typeId: number
  condition: string
  status: string
  notes: string | null
}

/** A pricing rule. validFrom is a campus date ("yyyy-MM-dd"); status is one of PRICING_STATUSES (computed on read). */
export interface PricingRuleDto {
  id: number
  roomType: string
  requesterRole: string
  hourlyRate: number
  isExempt: boolean
  validFrom: string
  status: string
}

/** One room type and requester role with the rule in effect today. The rule fields are null for a gap. */
export interface CurrentPricingRuleDto {
  roomType: string
  requesterRole: string
  id: number | null
  hourlyRate: number | null
  isExempt: boolean | null
  validFrom: string | null
}

/** hourlyRate is 0 when isExempt. validFrom is today (campus) or later. */
export interface PricingRuleRequest {
  roomType: string
  requesterRole: string
  hourlyRate: number
  isExempt: boolean
  validFrom: string
}

/** One booking-policy setting as stored: value is text (opening_hours is JSON text). updatedByName is null for seed values. */
export interface PolicySettingDto {
  key: string
  value: string
  valueType: string
  description: string
  updatedAt: string
  updatedByName: string | null
}

/** PUT /api/policy-settings: only the keys to change. */
export interface PolicySettingsUpdateRequest {
  settings: { key: string; value: string }[]
}

/** A booking request in a list. Times are UTC; clubName is null for an academic (lecturer) booking. */
export interface BookingRequestSummaryDto {
  id: number
  purpose: string
  status: string
  requestedStart: string
  requestedEnd: string
  attendees: number
  budgetLkr: number
  clubName: string | null
  requesterName: string
  requesterEmail: string
  /** Set by POST /api/booking-requests/{id}/cancel; null otherwise. */
  cancelledAt: string | null
  /** An owner's late cancellation of an approved booking (flagged, not charged). */
  isLateCancellation: boolean
  cancelledByOfficer: boolean
  createdAt: string
}

export interface RequesterDto {
  id: number
  name: string
  email: string
}

export interface ClubRefDto {
  id: number
  name: string
}

export interface RequiredFeatureDto {
  code: string
  name: string
}

export interface RequestedEquipmentDto {
  typeId: number
  typeCode: string
  typeName: string
  quantity: number
}

/** One status change. changedById and changedByName are null when the system made it. */
export interface RequestStatusHistoryDto {
  fromStatus: string | null
  toStatus: string
  changedById: number | null
  changedByName: string | null
  reason: string | null
  changedAt: string
}

/**
 * A booking request with everything the requester entered and its history, oldest first. notes is untrusted
 * requester text. latestProposal is always null until the agent workflow exists (Phase 3).
 */
export interface BookingRequestDetailDto {
  id: number
  purpose: string
  status: string
  attendees: number
  requestedStart: string
  requestedEnd: string
  budgetLkr: number
  notes: string | null
  requester: RequesterDto
  club: ClubRefDto | null
  requiredFeatures: RequiredFeatureDto[]
  equipment: RequestedEquipmentDto[]
  history: RequestStatusHistoryDto[]
  latestProposal: unknown
  cancelledAt: string | null
  isLateCancellation: boolean
  cancelledByOfficer: boolean
  createdAt: string
  updatedAt: string
}
