import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { useAuthStore } from '../../auth/authStore'
import { NOT_STAFF_MESSAGE, Roles } from '../../auth/roles'
import { makeAuth } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { INVALID_CREDENTIALS } from './LoginPage'

async function submit(user: ReturnType<typeof renderApp>['user'], email: string, password: string) {
  await user.type(screen.getByLabelText(/email/i), email)
  await user.type(screen.getByLabelText(/password/i), password)
  await user.click(screen.getByRole('button', { name: /sign in/i }))
}

describe('LoginPage', () => {
  it('shows validation errors on an empty submit', async () => {
    const { user } = renderApp('/login')

    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByText('Enter a valid email address')).toBeInTheDocument()
    expect(screen.getByText('Password is required')).toBeInTheDocument()
  })

  it('stores the token and navigates to the dashboard on success', async () => {
    server.use(http.post(`${API}/api/auth/login`, () => HttpResponse.json(makeAuth(Roles.FacilitiesOfficer))))
    const { user } = renderApp('/login')

    await submit(user, 'perera@campusspace.local', 'CampusSpace#2026')

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(useAuthStore.getState().token).toBe('token-FacilitiesOfficer')
    expect(localStorage.getItem('campusspace-auth')).toContain('token-FacilitiesOfficer')
  })

  it('shows "Invalid email or password" on 401', async () => {
    server.use(
      http.post(`${API}/api/auth/login`, () =>
        HttpResponse.json({ status: 401, title: INVALID_CREDENTIALS }, { status: 401 }),
      ),
    )
    const { user } = renderApp('/login')

    await submit(user, 'perera@campusspace.local', 'wrong')

    expect(await screen.findByText(INVALID_CREDENTIALS)).toBeInTheDocument()
    expect(useAuthStore.getState().token).toBeNull()
  })

  it.each([Roles.Student, Roles.LabTechnician])(
    'refuses a %s with the mobile-app message, keeps no session and stays on the login page',
    async (role) => {
      server.use(http.post(`${API}/api/auth/login`, () => HttpResponse.json(makeAuth(role))))
      const { user } = renderApp('/login')

      await submit(user, 'someone@campusspace.local', 'CampusSpace#2026')

      expect(await screen.findByText(NOT_STAFF_MESSAGE)).toBeInTheDocument()
      expect(useAuthStore.getState().token).toBeNull()
      expect(localStorage.getItem('campusspace-auth') ?? '').not.toContain(`token-${role}`)
      // No redirect happened: still the sign-in form, not a guarded page.
      await new Promise((resolve) => setTimeout(resolve, 100))
      expect(screen.getByText('Staff sign in')).toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: '403' })).not.toBeInTheDocument()
    },
  )

  it('shows a notice when the session expired', () => {
    renderApp('/login?expired=1')

    expect(screen.getByText(/session has expired/i)).toBeInTheDocument()
  })
})
