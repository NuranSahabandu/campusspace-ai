import { act, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { AUDIT_LOGS, BUILDINGS, FEATURES, ROOMS, makeAuth, pageOf } from '../test/fixtures'
import { API, server } from '../test/server'
import { renderApp } from '../test/utils'
import { useAuthStore } from './authStore'
import { type Role, Roles } from './roles'

type User = ReturnType<typeof renderApp>['user']

function officerPageHandlers() {
  server.use(
    http.get(`${API}/api/rooms`, () => HttpResponse.json(pageOf(ROOMS))),
    http.get(`${API}/api/buildings`, () => HttpResponse.json(BUILDINGS)),
    http.get(`${API}/api/features`, () => HttpResponse.json(FEATURES)),
    http.get(`${API}/api/audit-logs`, () => HttpResponse.json(pageOf(AUDIT_LOGS))),
  )
}

async function logOutFromMenu(user: User) {
  await user.click(await screen.findByRole('button', { name: 'Account menu' }))
  await user.click(await screen.findByRole('menuitem', { name: 'Log out' }))
  await screen.findByText('Staff sign in')
}

async function logInAs(user: User, role: Role) {
  server.use(http.post(`${API}/api/auth/login`, () => HttpResponse.json(makeAuth(role))))
  await user.type(screen.getByLabelText(/email/i), 'someone@campusspace.local')
  await user.type(screen.getByLabelText(/password/i), 'CampusSpace#2026')
  await user.click(screen.getByRole('button', { name: /sign in/i }))
}

describe('Where login returns to', () => {
  it('goes to the dashboard after logging out from /forbidden', async () => {
    const { user } = renderApp('/forbidden', { role: Roles.FacilitiesOfficer })
    await screen.findByRole('heading', { name: '403' })

    await logOutFromMenu(user)
    await logInAs(user, Roles.FacilitiesOfficer)

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('does not send the next user to a page their role cannot open', async () => {
    officerPageHandlers()
    const { user } = renderApp('/audit-logs', { role: Roles.Admin })
    await screen.findByRole('heading', { name: 'Audit log' })

    await logOutFromMenu(user)
    await logInAs(user, Roles.FacilitiesOfficer)

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('keeps no return path after an explicit logout, even from an allowed page', async () => {
    officerPageHandlers()
    const { user } = renderApp('/rooms', { role: Roles.FacilitiesOfficer })
    await screen.findByRole('heading', { name: 'Rooms' })

    await logOutFromMenu(user)
    await logInAs(user, Roles.FacilitiesOfficer)

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('returns to /rooms after the session expired there', async () => {
    officerPageHandlers()
    const { user } = renderApp('/rooms', { role: Roles.FacilitiesOfficer })
    await screen.findByRole('heading', { name: 'Rooms' })

    // What the expiry timer in authStore does.
    act(() => useAuthStore.getState().logout())
    await screen.findByText('Staff sign in')
    await logInAs(user, Roles.FacilitiesOfficer)

    expect(await screen.findByRole('heading', { name: 'Rooms' })).toBeInTheDocument()
  })
})
