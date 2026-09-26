import type { AuthResponse, ClubDetailDto, ClubDto, ClubMemberDto, PagedResult, UserDto } from '../api/types'
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
