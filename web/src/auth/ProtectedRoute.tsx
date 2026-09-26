import type { ReactNode } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { useAuthStore } from './authStore'
import { isReturnablePath } from './routeAccess'

interface Props {
  /** Roles allowed through. Omit to allow any signed-in user. */
  roles?: readonly string[]
  /** Content to render; defaults to the nested routes. */
  children?: ReactNode
}

/** Route guard (§12): anonymous → /login (remembering where they were going, see below); wrong role → /forbidden. */
export function ProtectedRoute({ roles, children }: Props) {
  const user = useAuthStore((s) => s.user)
  const token = useAuthStore((s) => s.token)
  const loggedOutByUser = useAuthStore((s) => s.loggedOutByUser)
  const location = useLocation()

  if (!token || !user) {
    // Remember the page only when the session ended on its own (expiry), and only if it is a real page:
    // never /forbidden or a 404. Router navigation runs in a transition, so on an explicit logout this
    // guard renders before AppLayout's navigate('/login') lands; loggedOutByUser keeps that from saving a path.
    const from = location.pathname + location.search
    const state = !loggedOutByUser && isReturnablePath(from) ? { from } : undefined
    return <Navigate to="/login" replace state={state} />
  }
  if (roles && !roles.includes(user.role)) {
    return <Navigate to="/forbidden" replace />
  }
  return children ?? <Outlet />
}
