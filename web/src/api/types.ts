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

/** A maintenance blackout as [start, end), both UTC. clashCount: active bookings it clashes with now (same rule as .../clashes). */
export interface BlackoutDto {
  id: number
  roomId: number
  start: string
  end: string
  reason: string
  createdById: number
  createdByName: string
  createdAt: string
  clashCount: number
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
  /** Student or Lecturer. */
  role: string
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
 * requester text. latestProposal summarises the live quote and its run (null while there is none).
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
  latestProposal: LatestProposalDto | null
  cancelledAt: string | null
  isLateCancellation: boolean
  cancelledByOfficer: boolean
  createdAt: string
  updatedAt: string
}

/**
 * The live (Draft or Issued) quote, its agent run and the proposed room: the same summary for owner and officer. Room
 * code and name come from Rooms (null only if the proposal couldn't be read).
 */
export interface LatestProposalDto {
  runId: string
  revisionNo: number
  roomId: number | null
  roomCode: string | null
  roomName: string | null
  quoteId: number
  total: number
  exempt: boolean
  quoteStatus: string
}

/** A quote from .NET (never the agent's numbers). Exempt quotes keep every line priced, with discount = subtotal. */
export interface QuotationDto {
  id: number | null
  requestId: number | null
  status: string | null
  lines: QuotationLineDto[]
  subtotal: number
  discount: number
  discountReason: string | null
  exempt: boolean
  total: number
  currency: string
}

export interface QuotationLineDto {
  kind: string
  description: string
  qty: number
  unitPrice: number
  lineTotal: number
}

/** GET /api/approvals/queue: one request waiting for the officer. draftTotal is null when it has no live quote. */
export interface ApprovalQueueItemDto {
  requestId: number
  purpose: string
  requesterName: string
  requesterRole: string
  clubName: string | null
  start: string
  end: string
  attendees: number
  proposedRoomCode: string | null
  draftTotal: number | null
  exempt: boolean
  revisionNo: number | null
  pendingSince: string
}

/** GET /api/booking-requests/{id}/agent-runs (newest first). */
export interface AgentRunSummaryDto {
  id: string
  revisionNo: number
  status: string
  failureReason: string | null
  startedAt: string | null
  completedAt: string | null
  durationMs: number | null
  model: string | null
  createdAt: string
}

/**
 * GET /api/agent-runs (Facilities Officer, the runs monitor). durationMs is the stored wall time (it includes the
 * officer's wait for a decided run). failureReason is cut to 200 characters plus "…". totalTokens is null when no
 * step reported usage. anyFallback: an LLM step fell back to its stub.
 */
export interface AgentRunListItemDto {
  id: string
  requestId: number
  purpose: string
  revisionNo: number
  status: string
  model: string | null
  startedAt: string | null
  completedAt: string | null
  durationMs: number | null
  failureReason: string | null
  stepCount: number
  toolCallCount: number
  totalTokens: number | null
  anyFallback: boolean
  createdAt: string
}

/**
 * GET /api/agent-runs/metrics?from=&to= (campus dates). Every rate and average is null when its denominator is 0.
 * Rates are fractions (0–1).
 */
export interface AgentRunMetricsDto {
  from: string | null
  to: string | null
  runs: RunMetricsDto
  agents: AgentMetricsDto[]
}

/** successRate = reachedGate ÷ finished; fallbackRate = fallbackRuns ÷ llmAttemptedRuns; avgTokensPerRun ÷ runsWithUsage. */
export interface RunMetricsDto {
  total: number
  byStatus: Record<string, number>
  inProgress: number
  finished: number
  reachedGate: number
  failedBeforeGate: number
  successRate: number | null
  reachedGateProcessing: ProcessingTimeDto
  failedBeforeGateProcessing: ProcessingTimeDto
  runsWithUsage: number
  totalTokens: number
  avgTokensPerRun: number | null
  llmAttemptedRuns: number
  fallbackRuns: number
  fallbackRate: number | null
}

/** Agent processing time (Σ step durations without finalize) of one bucket; runs is the denominator. */
export interface ProcessingTimeDto {
  runs: number
  withoutSteps: number
  avgMs: number | null
  p95Ms: number | null
}

/** Latency, failureRate and llmShare divide by steps; fallbackRate by llmAttemptedSteps; avgTokensPerStep by stepsWithUsage. */
export interface AgentMetricsDto {
  agent: string
  steps: number
  avgMs: number | null
  p95Ms: number | null
  failedSteps: number
  failureRate: number | null
  llmSteps: number
  llmShare: number | null
  skippedSteps: number
  llmAttemptedSteps: number
  fallbackSteps: number
  fallbackRate: number | null
  stepsWithUsage: number
  totalTokens: number
  avgTokensPerStep: number | null
}

/** Any JSON value, as stored by the agent trace. Shown only as pretty-printed text. */
export type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue }

/**
 * GET /api/agent-runs/{id} (Facilities Officer). validation is grouped by attempt, latest first. policyChangedKeys are
 * the policy keys whose current value differs from the run's snapshot (computed on the server).
 */
export interface AgentRunDetailDto {
  id: string
  requestId: number
  revisionNo: number
  status: string
  model: string | null
  nodes: string[]
  officerSummary: string | null
  failureReason: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  durationMs: number | null
  proposal: AgentProposalDto | null
  steps: AgentStepDto[]
  validation: ValidationAttemptDto[]
  decisions: ApprovalDecisionDto[]
  policySnapshot: JsonValue | null
  policyChangedKeys: string[]
}

export interface AgentProposalDto {
  chosen: VenueOptionDto
  alternatives: VenueOptionDto[]
  venueUnmet: string | null
  equipmentLines: ProposalEquipmentLineDto[]
  substitutions: SubstitutionDto[]
  equipmentUnmet: string[]
  policyFlags: string[]
}

/** reason is null for a chosen room the agent didn't list. */
export interface VenueOptionDto {
  roomId: number
  code: string
  name: string
  capacity: number | null
  building: string | null
  features: string[]
  reason: string | null
}

/** source: portable, room_builtin (qty 0, unpriced) or substitute. */
export interface ProposalEquipmentLineDto {
  typeCode: string
  qty: number
  source: string
}

export interface SubstitutionDto {
  requestedCode: string
  substituteCode: string
  qty: number
  reason: string
}

export interface AgentStepDto {
  sequence: number
  agentName: string
  status: string
  retries: number
  error: string | null
  durationMs: number
  input: JsonValue | null
  output: JsonValue | null
  toolCalls: AgentToolCallDto[]
}

export interface AgentToolCallDto {
  toolName: string
  args: JsonValue
  resultSummary: JsonValue | null
  succeeded: boolean
  error: string | null
  durationMs: number
}

export interface ValidationAttemptDto {
  attempt: number
  rules: { rule: string; passed: boolean; message: string | null }[]
}

/** decision: Approve, Reject or Revise. comment is untrusted officer text (plain text only). */
export interface ApprovalDecisionDto {
  decision: string
  officerName: string
  comment: string | null
  decidedAt: string
}

/** The 202 body of approve while the agent hasn't confirmed yet. */
export interface ApprovalInProgressDto {
  requestId: number
  status: 'ApprovalInProgress'
}

/**
 * An equipment loan (GET /api/loans). Times are UTC. isOverdue: still out and past dueAt. returnCondition is Good,
 * MinorWear or Damaged once checked in. damageNote is untrusted technician text. The photo, when hasPhoto, is served only
 * by GET /api/loans/{id}/photo behind auth.
 */
export interface LoanDto {
  id: number
  bookingId: number
  roomCode: string
  itemId: number
  assetTag: string
  typeCode: string
  checkedOutAt: string
  checkedOutByName: string
  dueAt: string
  checkedInAt: string | null
  checkedInByName: string | null
  returnCondition: string | null
  damageNote: string | null
  isLateReturn: boolean
  isOverdue: boolean
  hasPhoto: boolean
}

/**
 * GET /api/booking-requests/{id}/notifications (Facilities Officer, newest first): one email per status change (Task
 * 5.2). redirected: Email:RedirectAllTo received it instead of the recipient. error is a fixed server text; render it as
 * plain text.
 */
export interface NotificationLogDto {
  id: number
  kind: string
  channel: string
  status: string
  recipient: string
  redirected: boolean
  attempts: number
  createdAt: string
  lastAttemptAt: string | null
  sentAt: string | null
  error: string | null
}
