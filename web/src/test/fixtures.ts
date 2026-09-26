import type { AuthResponse, PagedResult, UserDto } from '../api/types'
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

export const usersPage = (items: UserDto[] = USERS): PagedResult<UserDto> => ({
  items,
  page: 1,
  pageSize: 20,
  total: items.length,
})
