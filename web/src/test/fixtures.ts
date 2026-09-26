import type {
  AuditLogDto,
  AuthResponse,
  BlackoutDto,
  BuildingDto,
  ClubDetailDto,
  ClubDto,
  ClubMemberDto,
  FeatureDto,
  PagedResult,
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
