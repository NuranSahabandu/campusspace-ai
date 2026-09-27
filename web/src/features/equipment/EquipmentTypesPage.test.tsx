import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { useToastStore } from '../../ui/toastStore'
import { equipmentHandlers } from './equipmentHandlers'
import { TYPE_IN_USE_MESSAGE } from './equipmentValues'

const rowOf = (code: string) => screen.getByText(code).closest('[role="row"]') as HTMLElement

describe('EquipmentTypesPage', () => {
  it('lists types with fees in LKR, item counts and the covering feature', async () => {
    const { typeRequests } = equipmentHandlers()
    renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('MIC-WIRELESS')).toBeInTheDocument()
    const mic = rowOf('MIC-WIRELESS')
    expect(within(mic).getByText('LKR 500.00')).toBeInTheDocument()
    expect(within(mic).getByRole('link', { name: '7 / 8' })).toHaveAttribute('href', '/equipment/items?typeId=1')
    expect(within(mic).getByText('1 under repair')).toBeInTheDocument()
    expect(within(mic).getByText('—')).toBeInTheDocument()

    const proj = rowOf('PROJ-PORTABLE')
    expect(within(proj).getByText('LKR 1,500.00')).toBeInTheDocument()
    expect(within(proj).getByText('Projector')).toBeInTheDocument()
    expect(within(proj).queryByText(/under repair/)).not.toBeInTheDocument()
    expect(within(rowOf('CLICKER')).getByRole('link', { name: '0 / 0' })).toBeInTheDocument()

    expect(typeRequests[0].get('sort')).toBe('code')
  })

  it('sends the category filter', async () => {
    const { typeRequests } = equipmentHandlers()
    const { user } = renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })
    await screen.findByText('MIC-WIRELESS')

    await user.click(screen.getByRole('combobox', { name: 'Category' }))
    await user.click(await screen.findByRole('option', { name: 'Visual' }))

    await waitFor(() => expect(typeRequests.at(-1)?.get('category')).toBe('Visual'))
    expect(typeRequests.at(-1)?.get('page')).toBe('1')
  })

  it('sorts by fee on the server', async () => {
    const { typeRequests } = equipmentHandlers()
    const { user } = renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })
    await screen.findByText('MIC-WIRELESS')

    await user.click(screen.getByRole('columnheader', { name: 'Fee' }))

    await waitFor(() => expect(typeRequests.at(-1)?.get('sort')).toBe('fee'))
  })

  it('shows an empty state when there are no types', async () => {
    server.use(
      http.get(`${API}/api/equipment-types/categories`, () => HttpResponse.json([])),
      http.get(`${API}/api/equipment-types`, () => HttpResponse.json({ items: [], page: 1, pageSize: 20, total: 0 })),
    )
    renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('No equipment types yet')).toBeInTheDocument()
  })

  it('shows an error with Retry when the list fails', async () => {
    let calls = 0
    server.use(
      http.get(`${API}/api/equipment-types/categories`, () => HttpResponse.json([])),
      http.get(`${API}/api/equipment-types`, () => {
        calls += 1
        return HttpResponse.json({ status: 500, title: 'An unexpected error occurred' }, { status: 500 })
      }),
    )
    const { user } = renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText(/Could not load equipment types/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(calls).toBe(2))
  })

  it('explains that a type with items cannot be deleted', async () => {
    equipmentHandlers()
    let deleted = false
    server.use(
      http.delete(`${API}/api/equipment-types/1`, () => {
        deleted = true
        return HttpResponse.json({ status: 409, title: 'In use' }, { status: 409 })
      }),
    )
    const { user } = renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'Delete MIC-WIRELESS' }))
    const dialog = await screen.findByRole('dialog', { name: 'Delete equipment type?' })
    expect(deleted).toBe(false)
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() =>
      expect(useToastStore.getState().current).toMatchObject({ severity: 'error', message: TYPE_IN_USE_MESSAGE }),
    )
    expect(deleted).toBe(true)
  })

  it('edits substitutes without offering the type itself', async () => {
    equipmentHandlers()
    let body: unknown
    server.use(
      http.get(`${API}/api/equipment-types/1/substitutes`, () =>
        HttpResponse.json([{ id: 3, code: 'CLICKER', name: 'Presentation clicker' }]),
      ),
      http.put(`${API}/api/equipment-types/1/substitutes`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json([])
      }),
    )
    const { user } = renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'Substitutes of MIC-WIRELESS' }))
    const dialog = await screen.findByRole('dialog', { name: 'Substitutes of MIC-WIRELESS' })
    expect(within(dialog).getByText("Types that can replace MIC-WIRELESS when it's short.")).toBeInTheDocument()
    const field = await within(dialog).findByRole('combobox', { name: 'Substitutes' })
    expect(within(dialog).getByRole('button', { name: 'CLICKER (Presentation clicker)' })).toBeInTheDocument()

    await user.click(field)
    const options = await screen.findAllByRole('option')
    expect(options.map((o) => o.textContent)).not.toContain('MIC-WIRELESS (Wireless microphone)')
    await user.click(screen.getByRole('option', { name: 'PROJ-PORTABLE (Portable projector)' }))
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(body).toEqual({ substituteTypeIds: [3, 2] }))
    await waitFor(() => expect(useToastStore.getState().current).toMatchObject({ message: 'Substitutes saved' }))
  })
})
