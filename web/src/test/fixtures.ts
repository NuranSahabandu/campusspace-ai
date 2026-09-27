import type {
  AuditLogDto,
  BookingRequestDetailDto,
  BookingRequestSummaryDto,
  AuthResponse,
  BlackoutDto,
  BuildingDto,
  ClubDetailDto,
  ClubDto,
  ClubMemberDto,
  CurrentPricingRuleDto,
  EquipmentItemDto,
  EquipmentTypeDto,
  FeatureDto,
  PagedResult,
  PolicySettingDto,
  PricingRuleDto,
  RoomDto,
  UserDto,
} from '../api/types'
import { Roles, type Role } from '../auth/roles'

export const makeUser = (role: Role, overrides: Partial<UserDto> = {}): UserDto => ({
  id: 1,
  fullName: `Test ${role}`,
  email: `${role.toLowerCase()}@campusspace.local`,
  role,
  isActive: true,
  createdAt: '2026-09-01T08:00:00Z',
  ...overrides,
})

export const makeAuth = (role: Role): AuthResponse => ({
  accessToken: `token-${role}`,
  expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
  user: makeUser(role),
})

export const USERS: UserDto[] = [
  makeUser(Roles.Student, { id: 1, fullName: 'Kavindi Perera', email: 'kavindi@campusspace.local' }),
  makeUser(Roles.FacilitiesOfficer, { id: 4, fullName: 'Mr. Perera', email: 'perera@campusspace.local' }),
  makeUser(Roles.Admin, { id: 5, fullName: 'System Admin', email: 'admin@campusspace.local' }),
]

export const pageOf = <T>(items: T[]): PagedResult<T> => ({ items, page: 1, pageSize: 20, total: items.length })

export const usersPage = (items: UserDto[] = USERS): PagedResult<UserDto> => pageOf(items)

export const CLUBS: ClubDto[] = [
  { id: 1, name: 'Robotics Club', isActive: true, memberCount: 3, representativeName: 'Kavindi Perera' },
  { id: 2, name: 'Chess Circle', isActive: false, memberCount: 0, representativeName: null },
]

const member = (userId: number, fullName: string, role: Role, isRepresentative = false): ClubMemberDto => ({
  userId,
  fullName,
  role,
  isRepresentative,
  joinedAt: '2026-09-01T08:00:00Z',
})

/** Robotics Club with Kavindi as representative, or Ishan once he is made representative. */
export const clubDetail = (representativeId = 1): ClubDetailDto => ({
  id: 1,
  name: 'Robotics Club',
  isActive: true,
  createdAt: '2026-09-01T08:00:00Z',
  updatedAt: '2026-09-01T08:00:00Z',
  members: [
    member(1, 'Kavindi Perera', Roles.Student, representativeId === 1),
    member(2, 'Ishan Silva', Roles.Student, representativeId === 2),
    member(3, 'Dr. Fernando', Roles.Lecturer, representativeId === 3),
  ].sort((a, b) => Number(b.isRepresentative) - Number(a.isRepresentative)),
})

export const AUDIT_LOGS: AuditLogDto[] = [
  {
    id: 3,
    userId: 5,
    userName: 'System Admin',
    action: 'Updated',
    entityType: 'User',
    entityId: '9',
    details: { changed: ['Role', 'IsActive'] },
    at: '2026-09-26T08:45:00Z',
  },
  {
    id: 2,
    userId: null,
    userName: null,
    action: 'LoginFailed',
    entityType: 'User',
    entityId: null,
    details: { email: 'nobody@campusspace.local' },
    at: '2026-09-26T08:30:00Z',
  },
  {
    id: 1,
    userId: 5,
    userName: 'System Admin',
    action: 'Deleted',
    entityType: 'ClubMember',
    entityId: '1:7',
    details: {},
    at: '2026-09-26T08:00:00Z',
  },
]

export const BUILDINGS: BuildingDto[] = [
  { id: 1, code: 'MB', name: 'Main Building', isActive: true },
  { id: 2, code: 'NB', name: 'New Building', isActive: true },
  { id: 3, code: 'OLD', name: 'Old Wing', isActive: false },
]

export const FEATURES: FeatureDto[] = [
  { id: 1, code: 'ac', name: 'Air conditioning' },
  { id: 2, code: 'computers', name: 'Computers' },
  { id: 3, code: 'projector', name: 'Projector' },
  { id: 4, code: 'whiteboard', name: 'Whiteboard' },
]

const ref = (code: string) => {
  const f = FEATURES.find((x) => x.code === code)!
  return { code: f.code, name: f.name }
}

export const makeRoom = (overrides: Partial<RoomDto> = {}): RoomDto => ({
  id: 1,
  code: 'A301',
  name: 'Computer Lab A301',
  type: 'ComputerLab',
  capacity: 48,
  isActive: true,
  building: { id: 1, code: 'MB', name: 'Main Building' },
  features: [ref('computers'), ref('projector')],
  ...overrides,
})

export const ROOMS: RoomDto[] = [
  makeRoom(),
  makeRoom({
    id: 2,
    code: 'N201',
    name: 'Computer Lab N201',
    capacity: 60,
    building: { id: 2, code: 'NB', name: 'New Building' },
  }),
  makeRoom({ id: 3, code: 'A102', name: 'Lecture Hall A102', type: 'LectureHall', capacity: 80, features: [ref('whiteboard')], isActive: false }),
]

export const BLACKOUTS: BlackoutDto[] = [
  {
    id: 7,
    roomId: 1,
    start: '2026-09-28T02:30:00Z',
    end: '2026-09-28T06:30:00Z',
    reason: 'Projector maintenance',
    createdById: 4,
    createdByName: 'Mr. Perera',
    createdAt: '2026-09-20T08:00:00Z',
  },
]

export const EQUIPMENT_CATEGORIES = ['Audio', 'Visual', 'Computing', 'Presentation', 'Accessory']

const counts = (available: number, onLoan: number, underRepair: number, retired: number) => ({
  total: available + onLoan + underRepair + retired,
  available,
  onLoan,
  underRepair,
  retired,
})

/** Mirrors the demo seed: MIC-WIRELESS (500, 7 of 8 available) and PROJ-PORTABLE (1500, covered by projector). */
export const EQUIPMENT_TYPES: EquipmentTypeDto[] = [
  {
    id: 1,
    code: 'MIC-WIRELESS',
    name: 'Wireless microphone',
    category: 'Audio',
    feePerBooking: 500,
    coveredByFeatureCode: null,
    coveredByFeatureName: null,
    itemCounts: counts(7, 0, 1, 0),
  },
  {
    id: 2,
    code: 'PROJ-PORTABLE',
    name: 'Portable projector',
    category: 'Visual',
    feePerBooking: 1500,
    coveredByFeatureCode: 'projector',
    coveredByFeatureName: 'Projector',
    itemCounts: counts(4, 1, 0, 0),
  },
  {
    id: 3,
    code: 'CLICKER',
    name: 'Presentation clicker',
    category: 'Presentation',
    feePerBooking: 100,
    coveredByFeatureCode: null,
    coveredByFeatureName: null,
    itemCounts: counts(0, 0, 0, 0),
  },
]

export const makeItem = (overrides: Partial<EquipmentItemDto> = {}): EquipmentItemDto => ({
  id: 1,
  assetTag: 'EQ-MICW-001',
  typeId: 1,
  typeCode: 'MIC-WIRELESS',
  typeName: 'Wireless microphone',
  condition: 'Good',
  status: 'Available',
  notes: null,
  updatedAt: '2026-09-26T08:45:00Z',
  ...overrides,
})

export const EQUIPMENT_ITEMS: EquipmentItemDto[] = [
  makeItem(),
  makeItem({ id: 2, assetTag: 'EQ-MICW-002', condition: 'Damaged', status: 'UnderRepair', notes: 'Cracked grille' }),
  makeItem({
    id: 3,
    assetTag: 'EQ-PROJ-001',
    typeId: 2,
    typeCode: 'PROJ-PORTABLE',
    typeName: 'Portable projector',
    condition: 'MinorWear',
    status: 'OnLoan',
    notes: 'Scratched lens cover',
  }),
  makeItem({ id: 4, assetTag: 'EQ-PROJ-002', typeId: 2, typeCode: 'PROJ-PORTABLE', typeName: 'Portable projector', status: 'Retired' }),
]

/** Campus "today" for pricing tests: 27 Sep 2026, 11:30 in Colombo. Pin it with vi.setSystemTime. */
export const PRICING_NOW = new Date('2026-09-27T06:00:00Z')

export const makePricingRule = (overrides: Partial<PricingRuleDto> = {}): PricingRuleDto => ({
  id: 1,
  roomType: 'ComputerLab',
  requesterRole: 'Student',
  hourlyRate: 1500,
  isExempt: false,
  validFrom: '2026-01-01',
  status: 'Current',
  ...overrides,
})

// Mirrors the seed (from 2026-01-01; lecturers exempt), plus one superseded and one scheduled rule.
export const PRICING_RULES: PricingRuleDto[] = [
  makePricingRule(),
  makePricingRule({ id: 5, requesterRole: 'Lecturer', hourlyRate: 0, isExempt: true }),
  makePricingRule({ id: 3, roomType: 'SeminarRoom', hourlyRate: 400, validFrom: '2025-06-01', status: 'Superseded' }),
  makePricingRule({ id: 7, roomType: 'SeminarRoom', hourlyRate: 500 }),
  makePricingRule({ id: 9, roomType: 'SeminarRoom', hourlyRate: 750, validFrom: '2026-11-01', status: 'Scheduled' }),
]

const current = (roomType: string, requesterRole: string, id: number | null, hourlyRate: number | null): CurrentPricingRuleDto => ({
  roomType,
  requesterRole,
  id,
  hourlyRate,
  isExempt: hourlyRate === null ? null : hourlyRate === 0,
  validFrom: id === null ? null : '2026-01-01',
})

/** GET /api/pricing-rules/current: Auditorium/Lecturer is a gap. */
export const CURRENT_PRICES: CurrentPricingRuleDto[] = [
  current('Auditorium', 'Student', 4, 3000),
  current('Auditorium', 'Lecturer', null, null),
  current('ComputerLab', 'Student', 1, 1500),
  current('ComputerLab', 'Lecturer', 5, 0),
  current('LectureHall', 'Student', 2, 1000),
  current('LectureHall', 'Lecturer', 6, 0),
  current('SeminarRoom', 'Student', 7, 500),
  current('SeminarRoom', 'Lecturer', 8, 0),
]

/** The seeded opening hours (PolicySettingDefaults.OpeningHoursJson). */
export const DEFAULT_OPENING_HOURS =
  '{"mon":{"open":"08:00","close":"20:00"},"tue":{"open":"08:00","close":"20:00"},"wed":{"open":"08:00","close":"20:00"},' +
  '"thu":{"open":"08:00","close":"20:00"},"fri":{"open":"08:00","close":"20:00"},"sat":{"open":"08:00","close":"16:00"},"sun":null}'

const setting = (key: string, valueType: string, value: string, description: string): PolicySettingDto => ({
  key,
  value,
  valueType,
  description,
  updatedAt: '2026-09-20T04:00:00Z',
  updatedByName: null,
})

/** The nine default settings (PolicySettingDefaults), in PolicyKeys.All order; one was changed by the officer. */
export const POLICY_SETTINGS: PolicySettingDto[] = [
  setting('opening_hours', 'json', DEFAULT_OPENING_HOURS, 'Opening hours per weekday in campus time (null = closed)'),
  setting('min_lead_time_hours', 'int', '48', 'Minimum hours between submitting a request and the booking start'),
  setting('max_advance_days_student', 'int', '60', 'How many days ahead a student can book'),
  setting('max_advance_days_lecturer', 'int', '90', 'How many days ahead a lecturer can book'),
  setting('max_duration_hours', 'int', '8', 'Longest booking, in hours'),
  {
    ...setting('max_capacity_ratio', 'decimal', '3', 'A room may seat at most this many times the attendees'),
    updatedAt: '2026-09-26T08:45:00Z',
    updatedByName: 'Mr. Perera',
  },
  setting('slot_granularity_minutes', 'int', '30', 'Booking start and end times fall on multiples of this many minutes'),
  setting(
    'free_cancellation_hours',
    'int',
    '24',
    'Cancelling more than this many hours before the start is free; later is flagged as late',
  ),
  setting('max_open_requests', 'int', '3', 'Most open requests a requester can have at once'),
]

/** Active clubs as an officer reads them from GET /api/clubs (the request list's Club filter). */
export const REQUEST_CLUBS: ClubDto[] = [
  { id: 2, name: 'Drama Society', isActive: true, memberCount: 2, representativeName: 'Nethmi Rajapaksa' },
  { id: 1, name: 'Robotics Club', isActive: true, memberCount: 3, representativeName: 'Kavindi Perera' },
]

/**
 * Booking request details shaped like GET /api/booking-requests/{id}: the two Development seed requests (Drama Society
 * rehearsal, guest lecture) and a Robotics Club workshop waiting for approval, whose notes contain markup and a newline.
 */
export const BOOKING_REQUEST_DETAILS: BookingRequestDetailDto[] = [
  {
    id: 1,
    purpose: 'Drama Society rehearsal',
    status: 'Submitted',
    attendees: 30,
    requestedStart: '2026-10-26T04:30:00Z',
    requestedEnd: '2026-10-26T06:30:00Z',
    budgetLkr: 3000,
    notes: null,
    requester: { id: 7, name: 'Nethmi Rajapaksa', email: 'nethmi@campusspace.local' },
    club: { id: 2, name: 'Drama Society' },
    requiredFeatures: [
      { code: 'ac', name: 'Air conditioning' },
      { code: 'smart_board', name: 'Smart board' },
    ],
    equipment: [{ typeId: 3, typeCode: 'MIC-WIRED', typeName: 'Wired microphone', quantity: 1 }],
    history: [
      {
        fromStatus: null,
        toStatus: 'Submitted',
        changedById: 7,
        changedByName: 'Nethmi Rajapaksa',
        reason: null,
        changedAt: '2026-09-27T13:02:47.677608Z',
      },
    ],
    latestProposal: null,
    createdAt: '2026-09-27T13:02:47.677815Z',
    updatedAt: '2026-09-27T13:02:47.677815Z',
  },
  {
    id: 2,
    purpose: 'Guest lecture: AI in agriculture',
    status: 'Submitted',
    attendees: 120,
    requestedStart: '2026-10-26T04:30:00Z',
    requestedEnd: '2026-10-26T06:30:00Z',
    budgetLkr: 0,
    notes: null,
    requester: { id: 2, name: 'Dr. Nimal Fernando', email: 'lecturer@campusspace.local' },
    club: null,
    requiredFeatures: [
      { code: 'projector', name: 'Projector' },
      { code: 'sound_system', name: 'Sound system' },
    ],
    equipment: [{ typeId: 1, typeCode: 'MIC-WIRELESS', typeName: 'Wireless microphone', quantity: 2 }],
    history: [
      {
        fromStatus: null,
        toStatus: 'Submitted',
        changedById: 2,
        changedByName: 'Dr. Nimal Fernando',
        reason: null,
        changedAt: '2026-09-27T13:02:47.677608Z',
      },
    ],
    latestProposal: null,
    createdAt: '2026-09-27T13:02:47.677815Z',
    updatedAt: '2026-09-27T13:02:47.677815Z',
  },
  {
    id: 3,
    purpose: 'Robotics Club Arduino workshop',
    status: 'PendingApproval',
    attendees: 40,
    requestedStart: '2026-10-20T08:30:00Z',
    requestedEnd: '2026-10-20T11:30:00Z',
    budgetLkr: 12500.5,
    notes: 'Please keep <b>bold</b> as typed.\nWe need extension cords.',
    requester: { id: 1, name: 'Kavindi Perera', email: 'kavindi@campusspace.local' },
    club: { id: 1, name: 'Robotics Club' },
    requiredFeatures: [{ code: 'projector', name: 'Projector' }],
    equipment: [
      { typeId: 1, typeCode: 'MIC-WIRELESS', typeName: 'Wireless microphone', quantity: 1 },
      { typeId: 2, typeCode: 'PROJ-PORTABLE', typeName: 'Portable projector', quantity: 2 },
    ],
    history: [
      {
        fromStatus: null,
        toStatus: 'Submitted',
        changedById: 1,
        changedByName: 'Kavindi Perera',
        reason: null,
        changedAt: '2026-09-27T09:00:00Z',
      },
      {
        fromStatus: 'Submitted',
        toStatus: 'AgentProcessing',
        changedById: null,
        changedByName: null,
        reason: null,
        changedAt: '2026-09-27T09:00:05Z',
      },
      {
        fromStatus: 'AgentProcessing',
        toStatus: 'PendingApproval',
        changedById: null,
        changedByName: null,
        reason: 'Proposal ready for review',
        changedAt: '2026-09-27T09:01:10Z',
      },
    ],
    latestProposal: null,
    createdAt: '2026-09-27T09:00:00Z',
    updatedAt: '2026-09-27T09:01:10Z',
  },
]

/** The list rows for BOOKING_REQUEST_DETAILS (the MSW handler returns them whatever the filters). */
export const BOOKING_REQUESTS: BookingRequestSummaryDto[] = [...BOOKING_REQUEST_DETAILS]
  .reverse()
  .map((r) => ({
    id: r.id,
    purpose: r.purpose,
    status: r.status,
    requestedStart: r.requestedStart,
    requestedEnd: r.requestedEnd,
    attendees: r.attendees,
    budgetLkr: r.budgetLkr,
    clubName: r.club?.name ?? null,
    requesterName: r.requester.name,
    requesterEmail: r.requester.email,
    createdAt: r.createdAt,
  }))
