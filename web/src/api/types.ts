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

/** Times carry an offset (campusLocalToIso gives +05:30); the server stores UTC. */
export interface CreateBlackoutRequest {
  start: string
  end: string
  reason: string
}
