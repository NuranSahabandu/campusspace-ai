import type { ReactNode } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { useAuthStore } from './authStore'

interface Props {
  /** Roles allowed through. Omit to allow any signed-in user. */
  roles?: readonly string[]
  /** Content to render; defaults to the nested routes. */
  children?: ReactNode
}

/** Route guard (§12): anonymous → /login (remembering where they were going); wrong role → /forbidden. */
export function ProtectedRoute({ roles, children }: Props) {
  const user = useAuthStore((s) => s.user)
  const token = useAuthStore((s) => s.token)
  const location = useLocation()

  if (!token || !user) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }
  if (roles && !roles.includes(user.role)) {
    return <Navigate to="/forbidden" replace />
  }
  return children ?? <Outlet />
}
