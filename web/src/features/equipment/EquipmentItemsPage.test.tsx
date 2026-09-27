import { screen, waitFor, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { renderApp } from '../../test/utils'
import { equipmentHandlers } from './equipmentHandlers'

const rowOf = (tag: string) => screen.getByText(tag).closest('[role="row"]') as HTMLElement

describe('EquipmentItemsPage', () => {
  it('lists items with type, condition and status chips', async () => {
    const { itemRequests } = equipmentHandlers()
    renderApp('/equipment/items', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('EQ-MICW-001')).toBeInTheDocument()
    const damaged = rowOf('EQ-MICW-002')
    expect(within(damaged).getByText('MIC-WIRELESS — Wireless microphone')).toBeInTheDocument()
    expect(within(damaged).getByText('Damaged')).toBeInTheDocument()
    expect(within(damaged).getByText('Under repair')).toBeInTheDocument()
    expect(within(damaged).getByText('Cracked grille')).toHaveAttribute('title', 'Cracked grille')
    expect(within(rowOf('EQ-PROJ-001')).getByText('On loan')).toBeInTheDocument()
    expect(within(rowOf('EQ-MICW-001')).getByText('26 Sept 2026, 14:15')).toBeInTheDocument()
    expect(itemRequests[0].get('sort')).toBe('assetTag')
    expect(itemRequests[0].has('typeId')).toBe(false)
  })

  it('sends search, status, condition and sort to the server', async () => {
    const { itemRequests } = equipmentHandlers()
    const { user } = renderApp('/equipment/items', { role: Roles.FacilitiesOfficer })
    await screen.findByText('EQ-MICW-001')

    await user.click(screen.getByRole('combobox', { name: 'Status' }))
    await user.click(await screen.findByRole('option', { name: 'Under repair' }))
    await waitFor(() => expect(itemRequests.at(-1)?.get('status')).toBe('UnderRepair'))

    await user.click(screen.getByRole('combobox', { name: 'Condition' }))
    await user.click(await screen.findByRole('option', { name: 'Damaged' }))
    await waitFor(() => expect(itemRequests.at(-1)?.get('condition')).toBe('Damaged'))

    await user.type(screen.getByLabelText('Search asset tag'), 'micw')
    await waitFor(() => expect(itemRequests.at(-1)?.get('search')).toBe('micw'))

    await user.click(screen.getByRole('columnheader', { name: 'Updated' }))
    await waitFor(() => expect(itemRequests.at(-1)?.get('sort')).toBe('updatedAt'))
    expect(itemRequests.at(-1)?.get('status')).toBe('UnderRepair')
  })

  it('takes the type filter from ?typeId=', async () => {
    const { itemRequests } = equipmentHandlers()
    const { user } = renderApp('/equipment/items?typeId=2', { role: Roles.FacilitiesOfficer })

    await screen.findByText('EQ-MICW-001')
    expect(itemRequests[0].get('typeId')).toBe('2')
    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Type' })).toHaveTextContent('PROJ-PORTABLE — Portable projector'),
    )

    await user.click(screen.getByRole('combobox', { name: 'Type' }))
    await user.click(await screen.findByRole('option', { name: 'All types' }))
    await waitFor(() => expect(itemRequests.at(-1)?.has('typeId')).toBe(false))
  })

  it('ignores a ?typeId= that is not a positive whole number', async () => {
    const { itemRequests } = equipmentHandlers()
    renderApp('/equipment/items?typeId=abc', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('EQ-MICW-001')).toBeInTheDocument()
    expect(itemRequests.length).toBeGreaterThan(0)
    expect(itemRequests.every((p) => !p.has('typeId'))).toBe(true)
    // The filter is empty ("All types"), with no "Type #…" fallback for the bad value.
    expect(screen.getByRole('combobox', { name: 'Type' })).not.toHaveTextContent(/Type #|—/)
  })
})
