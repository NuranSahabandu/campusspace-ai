import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { BUILDINGS, FEATURES, ROOMS, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'

function listHandlers() {
  server.use(
    http.get(`${API}/api/buildings`, () => HttpResponse.json(BUILDINGS)),
    http.get(`${API}/api/features`, () => HttpResponse.json(FEATURES)),
    http.get(`${API}/api/rooms`, () => HttpResponse.json(pageOf(ROOMS))),
  )
}

async function openNewRoom() {
  const rendered = renderApp('/rooms', { role: Roles.FacilitiesOfficer })
  await rendered.user.click(await screen.findByRole('button', { name: 'New room' }))
  const dialog = await screen.findByRole('dialog', { name: 'New room' })
  return { ...rendered, dialog }
}

/** Fills a valid new room: SR1 in MB, 30 seats, with a projector. */
async function fillValidRoom(user: ReturnType<typeof renderApp>['user'], dialog: HTMLElement) {
  await user.type(within(dialog).getByLabelText('Code'), 'SR1')
  await user.type(within(dialog).getByLabelText('Name'), 'Seminar Room 1')
  await user.click(within(dialog).getByRole('combobox', { name: 'Building' }))
  await user.click(await screen.findByRole('option', { name: 'MB · Main Building' }))
  await user.click(within(dialog).getByRole('combobox', { name: 'Type' }))
  await user.click(await screen.findByRole('option', { name: 'Seminar room' }))
  await user.type(within(dialog).getByLabelText('Capacity'), '30')
  await user.click(within(dialog).getByRole('combobox', { name: 'Features' }))
  await user.click(await screen.findByRole('option', { name: 'projector (Projector)' }))
}

describe('RoomFormDialog', () => {
  it('validates an empty form before sending anything', async () => {
    listHandlers()
    let posted = false
    server.use(
      http.post(`${API}/api/rooms`, () => {
        posted = true
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const { user, dialog } = await openNewRoom()

    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Code is required')).toBeInTheDocument()
    expect(within(dialog).getByText('Name is required')).toBeInTheDocument()
    expect(within(dialog).getByText('Choose a building')).toBeInTheDocument()
    expect(within(dialog).getByText('Choose a type')).toBeInTheDocument()
    expect(within(dialog).getByText('Capacity is required')).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it('offers only active buildings for a new room', async () => {
    listHandlers()
    const { user, dialog } = await openNewRoom()

    await user.click(within(dialog).getByRole('combobox', { name: 'Building' }))
    expect(await screen.findByRole('option', { name: 'MB · Main Building' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'OLD · Old Wing' })).not.toBeInTheDocument()
  })

  it('shows a server 400 on FeatureCodes under the features field', async () => {
    listHandlers()
    let body: unknown
    server.use(
      http.post(`${API}/api/rooms`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(
          { status: 400, title: 'Unknown feature codes: projector.', errors: { FeatureCodes: ['Unknown feature codes: projector.'] } },
          { status: 400 },
        )
      }),
    )
    const { user, dialog } = await openNewRoom()
    await fillValidRoom(user, dialog)
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Unknown feature codes: projector.')).toBeInTheDocument()
    expect(within(dialog).getByRole('combobox', { name: 'Features' })).toHaveAttribute('aria-invalid', 'true')
    expect(body).toEqual({
      code: 'SR1',
      name: 'Seminar Room 1',
      type: 'SeminarRoom',
      capacity: 30,
      buildingId: 1,
      featureCodes: ['projector'],
    })
  })

  it('shows a 409 duplicate under the code field', async () => {
    listHandlers()
    server.use(
      http.post(`${API}/api/rooms`, () =>
        HttpResponse.json({ status: 409, title: 'Room code is already taken' }, { status: 409 }),
      ),
    )
    const { user, dialog } = await openNewRoom()
    await fillValidRoom(user, dialog)
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Room code is already taken')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Code')).toHaveAttribute('aria-invalid', 'true')
  })

  it('reactivates an inactive room from Edit', async () => {
    listHandlers()
    let body: Record<string, unknown> = {}
    server.use(
      http.put(`${API}/api/rooms/3`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({ ...ROOMS[2], isActive: true })
      }),
    )
    const { user } = renderApp('/rooms', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'Edit A102' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit A102' })
    await user.click(within(dialog).getByRole('switch', { name: 'Active' }))
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await screen.findByText('Room updated')
    expect(body).toMatchObject({ code: 'A102', buildingId: 1, featureCodes: ['whiteboard'], isActive: true })
  })
})
