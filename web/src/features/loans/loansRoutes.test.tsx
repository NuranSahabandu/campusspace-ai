import { screen, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { ROUTE_ROLES } from '../../auth/routeAccess'
import { renderApp } from '../../test/utils'
import { loansHandlers } from './loansHandlers'

describe('Loans routes', () => {
  it('shows Facilities Officers "Loans" after the equipment pages, opening the page', async () => {
    loansHandlers()
    const { user } = renderApp('/', { role: Roles.FacilitiesOfficer })
    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]
    const links = within(nav).getAllByRole('link').map((l) => l.textContent)
    expect(links.indexOf('Loans')).toBe(links.indexOf('Equipment items') + 1)

    await user.click(within(nav).getByRole('link', { name: 'Loans' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Loans' })).toBeInTheDocument()
  })

  it('hides Loans from Admins', async () => {
    renderApp('/forbidden', { role: Roles.Admin })
    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]

    expect(within(nav).queryByRole('link', { name: 'Loans' })).not.toBeInTheDocument()
  })

  it('sends an Admin on /loans to the 403 page', async () => {
    renderApp('/loans?overdue=true', { role: Roles.Admin })

    expect(await screen.findByRole('heading', { name: '403' })).toBeInTheDocument()
  })

  // Lab Technicians and requesters use the mobile app: the portal's staff guard (STAFF_ROLES) already keeps them out.
  it('is for Facilities Officers only', () => {
    expect(ROUTE_ROLES['/loans']).toEqual([Roles.FacilitiesOfficer])
  })
})
