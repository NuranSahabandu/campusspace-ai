import { screen, within } from '@testing-library/react'
import { Roles } from '../auth/roles'
import { renderApp } from '../test/utils'

describe('Admin-only routes', () => {
  it.each(['/clubs', '/clubs/1', '/audit-logs'])('sends a FacilitiesOfficer on %s to the 403 page', async (route) => {
    renderApp(route, { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { name: '403' })).toBeInTheDocument()
  })

  it('shows Clubs and Audit log in the drawer for Admins only', async () => {
    const { unmount } = renderApp('/forbidden', { role: Roles.FacilitiesOfficer })
    let nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]
    expect(within(nav).queryByText('Clubs')).not.toBeInTheDocument()
    expect(within(nav).queryByText('Audit log')).not.toBeInTheDocument()
    unmount()

    renderApp('/forbidden', { role: Roles.Admin })
    nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]
    expect(within(nav).getByText('Clubs')).toBeInTheDocument()
    expect(within(nav).getByText('Audit log')).toBeInTheDocument()
  })
})
