import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { AppRoutes } from '../App'
import { useAuthStore } from '../auth/authStore'
import type { Role } from '../auth/roles'
import { AppProviders } from '../providers'
import { createQueryClient } from '../queryClient'
import { makeAuth } from './fixtures'

/** Renders the whole route table at `route`, optionally signed in as `role`. */
export function renderApp(route: string, { role }: { role?: Role } = {}) {
  if (role) useAuthStore.getState().login(makeAuth(role))
  const user = userEvent.setup()
  const result = render(
    <AppProviders client={createQueryClient({ retry: false })}>
      <MemoryRouter initialEntries={[route]}>
        <AppRoutes />
      </MemoryRouter>
    </AppProviders>,
  )
  return { user, ...result }
}
