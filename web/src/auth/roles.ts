// Mirrors backend/CampusSpace.Api/Models/Roles.cs. Keep the two in sync.
export const Roles = {
  Student: 'Student',
  Lecturer: 'Lecturer',
  LabTechnician: 'LabTechnician',
  FacilitiesOfficer: 'FacilitiesOfficer',
  Admin: 'Admin',
} as const

export type Role = (typeof Roles)[keyof typeof Roles]

export const ALL_ROLES: readonly Role[] = Object.values(Roles)

/** Roles allowed into the staff web portal. Everyone else uses the mobile app. */
export const STAFF_ROLES: readonly Role[] = [Roles.FacilitiesOfficer, Roles.Admin]

export const isStaffRole = (role: string): boolean => (STAFF_ROLES as readonly string[]).includes(role)

/** What a signed-in requester or technician is told: the login refusal and the access-denied page use it. */
export const NOT_STAFF_MESSAGE = 'This portal is for staff. Please use the CampusSpace mobile app.'
