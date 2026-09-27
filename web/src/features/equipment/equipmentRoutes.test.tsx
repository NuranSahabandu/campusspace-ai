import { screen, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { renderApp } from '../../test/utils'
import { equipmentHandlers } from './equipmentHandlers'

describe('Equipment routes', () => {
  it('shows both equipment pages in the drawer for Facilities Officers, who can open them', async () => {
    equipmentHandlers()
    const { user } = renderApp('/', { role: Roles.FacilitiesOfficer })
    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]

    await user.click(within(nav).getByRole('link', { name: 'Equipment types' }))
    expect(await screen.findByRole('heading', { name: 'Equipment types' })).toBeInTheDocument()
    await user.click(within(nav).getByRole('link', { name: 'Equipment items' }))
    expect(await screen.findByRole('heading', { name: 'Equipment items' })).toBeInTheDocument()
  })

  it('sends /equipment to Equipment types', async () => {
    equipmentHandlers()
    renderApp('/equipment', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { name: 'Equipment types' })).toBeInTheDocument()
  })

  it('hides equipment from Admins', async () => {
    renderApp('/forbidden', { role: Roles.Admin })
    const nav = (await screen.findAllByRole('navigation', { name: 'Main navigation' }))[0]

    expect(within(nav).queryByText(/Equipment/)).not.toBeInTheDocument()
  })

  it.each(['/equipment', '/equipment/types', '/equipment/items', '/equipment/items?typeId=1'])(
    'sends an Admin on %s to the 403 page',
    async (route) => {
      renderApp(route, { role: Roles.Admin })

      expect(await screen.findByRole('heading', { name: '403' })).toBeInTheDocument()
    },
  )
})
