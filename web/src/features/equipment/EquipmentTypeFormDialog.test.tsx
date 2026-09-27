import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { equipmentHandlers } from './equipmentHandlers'

async function openNewType() {
  const rendered = renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })
  await rendered.user.click(await screen.findByRole('button', { name: 'New type' }))
  const dialog = await screen.findByRole('dialog', { name: 'New equipment type' })
  return { ...rendered, dialog }
}

/** Fills a valid new type: HDMI-CABLE, Accessory, LKR 50. */
async function fillValidType(user: ReturnType<typeof renderApp>['user'], dialog: HTMLElement, code = 'hdmi-cable') {
  await user.type(within(dialog).getByLabelText('Code'), code)
  await user.type(within(dialog).getByLabelText('Name'), 'HDMI cable')
  await user.click(within(dialog).getByRole('combobox', { name: 'Category' }))
  await user.click(await screen.findByRole('option', { name: 'Accessory' }))
  await user.type(within(dialog).getByLabelText('Fee per booking (LKR)'), '50')
}

/** Records whether anything was POSTed. */
function recordPosts() {
  const posts: unknown[] = []
  server.use(
    http.post(`${API}/api/equipment-types`, async ({ request }) => {
      posts.push(await request.json())
      return HttpResponse.json({}, { status: 201 })
    }),
  )
  return posts
}

describe('EquipmentTypeFormDialog', () => {
  it('upper-cases the code as you type and sends a valid type', async () => {
    equipmentHandlers()
    const posts = recordPosts()
    const { user, dialog } = await openNewType()

    expect(within(dialog).getByText('Leave empty unless rooms can have this built in')).toBeInTheDocument()
    await fillValidType(user, dialog)
    expect(within(dialog).getByLabelText('Code')).toHaveValue('HDMI-CABLE')
    await user.click(within(dialog).getByRole('combobox', { name: 'Covered by room feature' }))
    await user.click(await screen.findByRole('option', { name: 'Projector (projector)' }))
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    await waitFor(() =>
      expect(posts).toEqual([
        { code: 'HDMI-CABLE', name: 'HDMI cable', category: 'Accessory', feePerBooking: 50, coveredByFeatureCode: 'projector' },
      ]),
    )
  })

  it('checks the code pattern before sending', async () => {
    equipmentHandlers()
    const posts = recordPosts()
    const { user, dialog } = await openNewType()

    await fillValidType(user, dialog, 'mic wireless')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(
      await within(dialog).findByText('Use letters and digits in hyphen-separated parts, for example MIC-WIRELESS'),
    ).toBeInTheDocument()
    expect(posts).toEqual([])
  })

  it('rejects a fee with more than two decimals before sending', async () => {
    equipmentHandlers()
    const posts = recordPosts()
    const { user, dialog } = await openNewType()

    await fillValidType(user, dialog)
    await user.type(within(dialog).getByLabelText('Fee per booking (LKR)'), '.345')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Fee can have at most 2 decimal places.')).toBeInTheDocument()
    expect(posts).toEqual([])
  })

  it('validates an empty form', async () => {
    equipmentHandlers()
    const posts = recordPosts()
    const { user, dialog } = await openNewType()

    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('At least 2 characters')).toBeInTheDocument()
    expect(within(dialog).getByText('Name is required')).toBeInTheDocument()
    expect(within(dialog).getByText('Choose a category')).toBeInTheDocument()
    expect(within(dialog).getByText('Fee is required')).toBeInTheDocument()
    expect(posts).toEqual([])
  })

  it('shows a duplicate code under the code field', async () => {
    equipmentHandlers()
    server.use(
      http.post(`${API}/api/equipment-types`, () =>
        HttpResponse.json({ status: 409, title: 'Equipment type code is already taken' }, { status: 409 }),
      ),
    )
    const { user, dialog } = await openNewType()

    await fillValidType(user, dialog, 'mic-wireless')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Equipment type code is already taken')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Code')).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows a server field error on the fee field', async () => {
    equipmentHandlers()
    server.use(
      http.post(`${API}/api/equipment-types`, () =>
        HttpResponse.json(
          { status: 400, title: 'Fee can have at most 2 decimal places.', errors: { FeePerBooking: ['Server says no.'] } },
          { status: 400 },
        ),
      ),
    )
    const { user, dialog } = await openNewType()

    await fillValidType(user, dialog)
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Server says no.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Fee per booking (LKR)')).toHaveAttribute('aria-invalid', 'true')
  })

  it('locks the code when editing and still sends it', async () => {
    equipmentHandlers()
    let body: unknown
    server.use(
      http.put(`${API}/api/equipment-types/2`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({})
      }),
    )
    const { user } = renderApp('/equipment/types', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'Edit PROJ-PORTABLE' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit PROJ-PORTABLE' })
    expect(within(dialog).getByLabelText('Code')).toBeDisabled()
    expect(within(dialog).getByText("Codes can't be changed")).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Fee per booking (LKR)')).toHaveValue('1500')

    await user.click(within(dialog).getByRole('combobox', { name: 'Covered by room feature' }))
    await user.click(await screen.findByRole('option', { name: 'None' }))
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(body).toEqual({
        code: 'PROJ-PORTABLE',
        name: 'Portable projector',
        category: 'Visual',
        feePerBooking: 1500,
        coveredByFeatureCode: null,
      }),
    )
  })
})
