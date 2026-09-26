import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { BUILDINGS, FEATURES } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { useToastStore } from '../../ui/toastStore'
import { IN_USE_MESSAGE } from './useFacilities'

const listHandlers = [
  http.get(`${API}/api/buildings`, () => HttpResponse.json(BUILDINGS)),
  http.get(`${API}/api/features`, () => HttpResponse.json(FEATURES)),
]

describe('ReferencePage', () => {
  it('lists buildings with status and features', async () => {
    server.use(...listHandlers)
    renderApp('/facilities/reference', { role: Roles.FacilitiesOfficer })

    const buildings = await screen.findByRole('table', { name: 'Buildings' })
    expect(within(buildings).getByText('Main Building')).toBeInTheDocument()
    expect(within(buildings).getByText('Inactive')).toBeInTheDocument()
    expect(await screen.findByRole('table', { name: 'Features' })).toHaveTextContent('projector')
  })

  it('shows a clear toast when deleting a feature that rooms still use', async () => {
    let deleted = false
    server.use(
      ...listHandlers,
      http.delete(`${API}/api/features/3`, () => {
        deleted = true
        return HttpResponse.json({ status: 409, title: 'In use' }, { status: 409 })
      }),
    )
    const { user } = renderApp('/facilities/reference', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'Delete projector' }))
    const dialog = await screen.findByRole('dialog', { name: 'Delete feature?' })
    expect(deleted).toBe(false)
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() =>
      expect(useToastStore.getState().current).toMatchObject({ severity: 'error', message: IN_USE_MESSAGE }),
    )
    expect(deleted).toBe(true)
  })

  it('shows a duplicate building code under the code field', async () => {
    server.use(
      ...listHandlers,
      http.post(`${API}/api/buildings`, () =>
        HttpResponse.json({ status: 409, title: 'Building code is already taken' }, { status: 409 }),
      ),
    )
    const { user } = renderApp('/facilities/reference', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'New building' }))
    const dialog = await screen.findByRole('dialog', { name: 'New building' })
    await user.type(within(dialog).getByLabelText('Code'), 'mb')
    await user.type(within(dialog).getByLabelText('Name'), 'Main again')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Building code is already taken')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Code')).toHaveAttribute('aria-invalid', 'true')
  })

  it('checks the feature code is snake_case before sending', async () => {
    let posted = false
    server.use(
      ...listHandlers,
      http.post(`${API}/api/features`, () => {
        posted = true
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const { user } = renderApp('/facilities/reference', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'New feature' }))
    const dialog = await screen.findByRole('dialog', { name: 'New feature' })
    expect(within(dialog).getByText('lowercase_snake_case, used by the AI agents')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Code'), 'sound system')
    await user.type(within(dialog).getByLabelText('Name'), 'Sound system')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Use snake_case, for example sound_system')).toBeInTheDocument()
    expect(posted).toBe(false)
  })
})
