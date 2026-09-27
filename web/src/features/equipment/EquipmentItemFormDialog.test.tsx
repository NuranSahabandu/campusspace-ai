import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { equipmentHandlers } from './equipmentHandlers'

async function openNewItem() {
  const rendered = renderApp('/equipment/items', { role: Roles.FacilitiesOfficer })
  await rendered.user.click(await screen.findByRole('button', { name: 'New item' }))
  const dialog = await screen.findByRole('dialog', { name: 'New equipment item' })
  return { ...rendered, dialog }
}

async function openEdit(tag: string) {
  const rendered = renderApp('/equipment/items', { role: Roles.FacilitiesOfficer })
  await rendered.user.click(await screen.findByRole('button', { name: `Edit ${tag}` }))
  const dialog = await screen.findByRole('dialog', { name: `Edit ${tag}` })
  return { ...rendered, dialog }
}

async function fillNewItem(user: ReturnType<typeof renderApp>['user'], dialog: HTMLElement, tag = 'eq-micw-009') {
  await user.type(within(dialog).getByLabelText('Asset tag'), tag)
  await user.click(within(dialog).getByRole('combobox', { name: 'Type' }))
  await user.click(await screen.findByRole('option', { name: 'MIC-WIRELESS — Wireless microphone' }))
}

/** Records every item write. */
function recordWrites() {
  const writes: unknown[] = []
  const reply = async ({ request }: { request: Request }) => {
    writes.push(await request.json())
    return HttpResponse.json({}, { status: request.method === 'POST' ? 201 : 200 })
  }
  server.use(http.post(`${API}/api/equipment-items`, reply), http.put(`${API}/api/equipment-items/:id`, reply))
  return writes
}

describe('EquipmentItemFormDialog', () => {
  it('creates an item with an upper-cased tag and never offers On loan', async () => {
    equipmentHandlers()
    const writes = recordWrites()
    const { user, dialog } = await openNewItem()

    await fillNewItem(user, dialog)
    expect(within(dialog).getByLabelText('Asset tag')).toHaveValue('EQ-MICW-009')
    await user.click(within(dialog).getByRole('combobox', { name: 'Status' }))
    const options = (await screen.findAllByRole('option')).map((o) => o.textContent)
    expect(options).toEqual(['Available', 'Under repair', 'Retired'])
    await user.click(screen.getByRole('option', { name: 'Available' }))
    await user.type(within(dialog).getByLabelText('Notes'), 'Spare')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    await waitFor(() =>
      expect(writes).toEqual([{ assetTag: 'EQ-MICW-009', typeId: 1, condition: 'Good', status: 'Available', notes: 'Spare' }]),
    )
  })

  it('does not let a damaged item be available', async () => {
    equipmentHandlers()
    const writes = recordWrites()
    const { user, dialog } = await openNewItem()

    await fillNewItem(user, dialog)
    await user.click(within(dialog).getByRole('combobox', { name: 'Condition' }))
    await user.click(await screen.findByRole('option', { name: 'Damaged' }))
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Damaged items must be UnderRepair or Retired.')).toBeInTheDocument()
    expect(writes).toEqual([])
  })

  it('shows a duplicate asset tag under the tag field', async () => {
    equipmentHandlers()
    server.use(
      http.post(`${API}/api/equipment-items`, () =>
        HttpResponse.json({ status: 409, title: 'Asset tag is already taken' }, { status: 409 }),
      ),
    )
    const { user, dialog } = await openNewItem()

    await fillNewItem(user, dialog, 'EQ-MICW-001')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Asset tag is already taken')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Asset tag')).toHaveAttribute('aria-invalid', 'true')
  })

  it('locks the type when editing and sends the stored one', async () => {
    equipmentHandlers()
    const writes = recordWrites()
    const { user, dialog } = await openEdit('EQ-MICW-002')

    expect(within(dialog).getByRole('combobox', { name: 'Type' })).toHaveAttribute('aria-disabled', 'true')
    expect(within(dialog).getByText("An item's type can't be changed")).toBeInTheDocument()
    await user.click(within(dialog).getByRole('combobox', { name: 'Status' }))
    await user.click(await screen.findByRole('option', { name: 'Retired' }))
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(writes).toEqual([
        { assetTag: 'EQ-MICW-002', typeId: 1, condition: 'Damaged', status: 'Retired', notes: 'Cracked grille' },
      ]),
    )
  })

  it('lets only the notes of an item on loan change', async () => {
    equipmentHandlers()
    const writes = recordWrites()
    const { user, dialog } = await openEdit('EQ-PROJ-001')

    expect(within(dialog).getByText("This item is on loan. Only notes can be edited until it's checked in.")).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Asset tag')).toBeDisabled()
    for (const name of ['Type', 'Condition', 'Status'])
      expect(within(dialog).getByRole('combobox', { name })).toHaveAttribute('aria-disabled', 'true')
    expect(within(dialog).getByRole('combobox', { name: 'Status' })).toHaveTextContent('On loan')

    const notes = within(dialog).getByLabelText('Notes')
    expect(notes).toBeEnabled()
    await user.clear(notes)
    await user.type(notes, 'Returning Friday')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(writes).toEqual([
        { assetTag: 'EQ-PROJ-001', typeId: 2, condition: 'MinorWear', status: 'OnLoan', notes: 'Returning Friday' },
      ]),
    )
  })
})
