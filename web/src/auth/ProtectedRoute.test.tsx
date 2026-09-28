import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { usersPage } from '../test/fixtures'
import { API, server } from '../test/server'
import { renderApp } from '../test/utils'
import { useAuthStore } from './authStore'
import { NOT_STAFF_MESSAGE, Roles } from './roles'

describe('ProtectedRoute', () => {
  it('sends an anonymous user to /login', () => {
    renderApp('/users')

    expect(screen.getByText('Staff sign in')).toBeInTheDocument()
  })

  it('sends a FacilitiesOfficer on /users to the 403 page', () => {
    renderApp('/users', { role: Roles.FacilitiesOfficer })

    expect(screen.getByRole('heading', { name: '403' })).toBeInTheDocument()
  })

  it('renders /users for an Admin', async () => {
    server.use(http.get(`${API}/api/users`, () => HttpResponse.json(usersPage())))
    renderApp('/users', { role: Roles.Admin })

    expect(await screen.findByRole('heading', { name: 'Users' })).toBeInTheDocument()
    expect(await screen.findByText('Mr. Perera')).toBeInTheDocument()
  })

  it('hides the Users nav item from a FacilitiesOfficer', () => {
    renderApp('/', { role: Roles.FacilitiesOfficer })

    const nav = screen.getAllByRole('navigation', { name: 'Main navigation' })[0]
    expect(nav).toHaveTextContent('Dashboard')
    expect(nav).not.toHaveTextContent('Users')
  })

  it('sends an anonymous user on /forbidden to /login', () => {
    renderApp('/forbidden')

    expect(screen.getByText('Staff sign in')).toBeInTheDocument()
  })

  it('shows staff the access-denied page with a dashboard link and Sign out', () => {
    renderApp('/users', { role: Roles.FacilitiesOfficer })

    expect(screen.getByText('You do not have permission to view this page.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to dashboard' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  })

  // A requester or technician session (the login form refuses them, but a stored session can remain) must land on the
  // access-denied page once. /forbidden used to sit inside the staff guard, which redirected it to itself forever.
  describe.each([Roles.Student, Roles.LabTechnician])('a signed-in %s', (role) => {
    it.each(['/', '/loans', '/requests/1', '/forbidden', '/no-such-page'])('lands on the access-denied page from %s', async (route) => {
      renderApp(route, { role })

      expect(screen.getByRole('heading', { name: '403' })).toBeInTheDocument()
      expect(screen.getByText(NOT_STAFF_MESSAGE)).toBeInTheDocument()
      expect(screen.queryByRole('link', { name: 'Back to dashboard' })).not.toBeInTheDocument()
      const nav = screen.getAllByRole('navigation', { name: 'Main navigation' })[0]
      expect(within(nav).queryAllByRole('link')).toHaveLength(0)
      // Still there a moment later: no redirect loop.
      await new Promise((resolve) => setTimeout(resolve, 100))
      expect(screen.getAllByRole('heading', { name: '403' })).toHaveLength(1)
    })

    it('can sign out from the access-denied page', async () => {
      const { user } = renderApp('/loans', { role })

      await user.click(screen.getByRole('button', { name: 'Sign out' }))

      expect(await screen.findByText('Staff sign in')).toBeInTheDocument()
      expect(useAuthStore.getState().token).toBeNull()
    })
  })
})
