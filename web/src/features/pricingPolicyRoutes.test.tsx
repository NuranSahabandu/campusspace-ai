import { screen, within } from '@testing-library/react'
import { Roles } from '../auth/roles'
import { renderApp } from '../test/utils'
import { policyHandlers } from './policy/policyHandlers'
import { pricingHandlers } from './pricing/pricingHandlers'

describe('Pricing and Booking policy routes', () => {
  it('shows both pages in the drawer for Facilities Officers, who can open them', async () => {
    pricingHandlers()
    policyHandlers()
    const { user } = renderApp('/', { role: Roles.FacilitiesOfficer })
    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]

    await user.click(within(nav).getByRole('link', { name: 'Pricing' }))
    expect(await screen.findByRole('heading', { name: 'Pricing' })).toBeInTheDocument()
    await user.click(within(nav).getByRole('link', { name: 'Booking policy' }))
    expect(await screen.findByRole('heading', { name: 'Booking policy' })).toBeInTheDocument()
  })

  it('hides both pages from Admins, whom the API would refuse (403)', async () => {
    renderApp('/forbidden', { role: Roles.Admin })
    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]

    expect(within(nav).queryByRole('link', { name: 'Pricing' })).not.toBeInTheDocument()
    expect(within(nav).queryByRole('link', { name: 'Booking policy' })).not.toBeInTheDocument()
  })

  it.each(['/pricing', '/policy'])('sends an Admin on %s to the 403 page', async (route) => {
    renderApp(route, { role: Roles.Admin })

    expect(await screen.findByRole('heading', { name: '403' })).toBeInTheDocument()
  })
})
