import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { usersPage } from '../test/fixtures'
import { API, server } from '../test/server'
import { renderApp } from '../test/utils'
import { Roles } from './roles'

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
})
