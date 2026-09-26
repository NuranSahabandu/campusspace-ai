import { matchPath } from 'react-router'
import { type Role, Roles, STAFF_ROLES } from './roles'

const ADMIN: readonly Role[] = [Roles.Admin]
// The facilities write endpoints are Officer-only, so Admins do not get these screens.
const OFFICER: readonly Role[] = [Roles.FacilitiesOfficer]

/**
 * Every page a signed-in user can open, with the roles allowed on it. The one source for the route guards
 * (App.tsx), the drawer (navItems.tsx) and where to go after login. /login, /forbidden and unknown paths
 * are deliberately absent: they are never somewhere to return to.
 */
export const ROUTE_ROLES = {
  '/': STAFF_ROLES,
  '/rooms': OFFICER,
  '/rooms/:id': OFFICER,
  '/facilities/reference': OFFICER,
  '/equipment': STAFF_ROLES,
  '/requests': STAFF_ROLES,
  '/approvals': STAFF_ROLES,
  '/agent-runs': STAFF_ROLES,
  '/users': ADMIN,
  '/clubs': ADMIN,
  '/clubs/:id': ADMIN,
  '/audit-logs': ADMIN,
} as const satisfies Record<string, readonly Role[]>

export type AppPath = keyof typeof ROUTE_ROLES

/** The roles allowed on a pathname, or null when it is not a page worth returning to. */
export function rolesForPath(pathname: string): readonly Role[] | null {
  const pattern = (Object.keys(ROUTE_ROLES) as AppPath[]).find((p) => matchPath(p, pathname))
  return pattern ? ROUTE_ROLES[pattern] : null
}

/** A location (path plus query) worth saving as the "return to" path: one of the app's pages. */
export function isReturnablePath(from: string): boolean {
  // Only same-app absolute paths; "//host" would be another site.
  if (!from.startsWith('/') || from.startsWith('//')) return false
  return rolesForPath(new URL(from, 'http://app').pathname) !== null
}

/** Where to go after login: `from` if this role may open it, otherwise the dashboard. */
export function returnPathFor(from: string | undefined, role: string): string {
  if (!from || !isReturnablePath(from)) return '/'
  const roles = rolesForPath(new URL(from, 'http://app').pathname) as readonly string[]
  return roles.includes(role) ? from : '/'
}
