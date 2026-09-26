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
