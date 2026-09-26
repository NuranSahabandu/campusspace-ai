import { screen, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { renderApp } from '../../test/utils'

describe('Facilities routes', () => {
  it.each(['/rooms', '/rooms/1', '/facilities/reference'])('sends an Admin on %s to the 403 page', async (route) => {
    renderApp(route, { role: Roles.Admin })

    expect(await screen.findByRole('heading', { name: '403' })).toBeInTheDocument()
  })

  it('shows Rooms and Buildings & features in the drawer for Facilities Officers only', async () => {
    const { unmount } = renderApp('/forbidden', { role: Roles.Admin })
    let nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]
    expect(within(nav).queryByText('Rooms')).not.toBeInTheDocument()
    expect(within(nav).queryByText('Buildings & features')).not.toBeInTheDocument()
    unmount()

    renderApp('/forbidden', { role: Roles.FacilitiesOfficer })
    nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]
    expect(within(nav).getByText('Rooms')).toBeInTheDocument()
    expect(within(nav).getByText('Buildings & features')).toBeInTheDocument()
  })
})
