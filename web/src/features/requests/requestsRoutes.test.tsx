import { screen, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { NAV_ITEMS } from '../../layout/navItems'
import { renderApp } from '../../test/utils'
import { requestsHandlers } from './requestsHandlers'

describe('Booking request routes', () => {
  it('shows Facilities Officers "Booking requests" first in their section, opening the list and a request', async () => {
    requestsHandlers()
    const { user } = renderApp('/', { role: Roles.FacilitiesOfficer })
    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]
    const links = within(nav).getAllByRole('link').map((l) => l.textContent)
    expect(links.slice(0, 3)).toEqual(['Dashboard', 'Booking requests', 'Rooms'])

    await user.click(within(nav).getByRole('link', { name: 'Booking requests' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Booking requests' })).toBeInTheDocument()
    await user.click(await screen.findByText('Drama Society rehearsal'))
    expect(await screen.findByRole('heading', { level: 1, name: 'Drama Society rehearsal' })).toBeInTheDocument()
  })

  it('has no old "Requests" placeholder entry', () => {
    expect(NAV_ITEMS.map((i) => i.label)).not.toContain('Requests')
  })

  it('hides booking requests from Admins', async () => {
    renderApp('/forbidden', { role: Roles.Admin })
    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]

    expect(within(nav).queryByText(/requests/i)).not.toBeInTheDocument()
  })

  it.each(['/requests', '/requests/1', '/requests?group=closed'])('sends an Admin on %s to the 403 page', async (route) => {
    renderApp(route, { role: Roles.Admin })

    expect(await screen.findByRole('heading', { name: '403' })).toBeInTheDocument()
  })
})
