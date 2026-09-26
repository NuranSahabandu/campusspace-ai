import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { BLACKOUTS, BUILDINGS, FEATURES, makeRoom, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'

/** Room 1 (A301) with its blackouts; records each blackouts query. */
function roomHandlers() {
  const requests: URLSearchParams[] = []
  server.use(
    http.get(`${API}/api/rooms/1`, () => HttpResponse.json(makeRoom())),
    http.get(`${API}/api/buildings`, () => HttpResponse.json(BUILDINGS)),
    http.get(`${API}/api/features`, () => HttpResponse.json(FEATURES)),
    http.get(`${API}/api/rooms/1/blackouts`, ({ request }) => {
      requests.push(new URL(request.url).searchParams)
      return HttpResponse.json(pageOf(BLACKOUTS))
    }),
  )
  return requests
}

async function openAddBlackout() {
  const rendered = renderApp('/rooms/1', { role: Roles.FacilitiesOfficer })
  await rendered.user.click(await screen.findByRole('button', { name: 'Add blackout' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add blackout to A301' })
  return { ...rendered, dialog }
}

// datetime-local inputs take a whole value; typing them key by key is not supported in jsdom.
const setValue = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } })

describe('RoomDetailPage', () => {
  it('shows the room and its upcoming blackouts in campus time', async () => {
    const requests = roomHandlers()
    renderApp('/rooms/1', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { name: 'A301 · Computer Lab A301' })).toBeInTheDocument()
    expect(screen.getByText('MB · Main Building')).toBeInTheDocument()
    expect(screen.getByText('computers')).toBeInTheDocument()
    expect(screen.getByText('Clashing bookings will be flagged here in Phase 2.')).toBeInTheDocument()
    // 02:30Z–06:30Z is 08:00–12:00 in Colombo.
    expect(await screen.findByText('Projector maintenance')).toBeInTheDocument()
    expect(screen.getByText(/08:00/)).toBeInTheDocument()
    expect(screen.getByText(/12:00/)).toBeInTheDocument()
    expect(screen.getByText('Mr. Perera')).toBeInTheDocument()
    expect(requests[0].get('from')).toMatch(/Z$/)
  })

  it('drops the from filter when Show past is on', async () => {
    const requests = roomHandlers()
    const { user } = renderApp('/rooms/1', { role: Roles.FacilitiesOfficer })
    await screen.findByText('Projector maintenance')

    await user.click(screen.getByRole('switch', { name: 'Show past' }))

    await waitFor(() => expect(requests.at(-1)?.has('from')).toBe(false))
  })

  it('shows "Room not found" for a 404', async () => {
    server.use(http.get(`${API}/api/rooms/99`, () => HttpResponse.json({ status: 404, title: 'Not Found' }, { status: 404 })))
    renderApp('/rooms/99', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Room not found')).toBeInTheDocument()
  })

  it('blocks a blackout whose end is before its start', async () => {
    roomHandlers()
    let posted = false
    server.use(
      http.post(`${API}/api/rooms/1/blackouts`, () => {
        posted = true
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const { user, dialog } = await openAddBlackout()

    setValue(within(dialog).getByLabelText('Start'), '2026-09-28T12:00')
    setValue(within(dialog).getByLabelText('End'), '2026-09-28T08:00')
    await user.type(within(dialog).getByLabelText('Reason'), 'Painting')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    expect(await within(dialog).findByText('End must be after start')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('End')).toHaveAttribute('aria-invalid', 'true')
    expect(posted).toBe(false)
  })

  it('sends campus-time ISO strings for a valid blackout', async () => {
    roomHandlers()
    let body: unknown
    server.use(
      http.post(`${API}/api/rooms/1/blackouts`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(BLACKOUTS[0], { status: 201 })
      }),
    )
    const { user, dialog } = await openAddBlackout()

    setValue(within(dialog).getByLabelText('Start'), '2026-09-28T08:00')
    setValue(within(dialog).getByLabelText('End'), '2026-09-28T12:30')
    await user.type(within(dialog).getByLabelText('Reason'), 'Painting')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    await waitFor(() =>
      expect(body).toEqual({ start: '2026-09-28T08:00:00+05:30', end: '2026-09-28T12:30:00+05:30', reason: 'Painting' }),
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('shows a server 400 on End under the end field', async () => {
    roomHandlers()
    server.use(
      http.post(`${API}/api/rooms/1/blackouts`, () =>
        HttpResponse.json(
          { status: 400, title: 'One or more validation errors occurred.', errors: { End: ['End must be after Start.'] } },
          { status: 400 },
        ),
      ),
    )
    const { user, dialog } = await openAddBlackout()

    setValue(within(dialog).getByLabelText('Start'), '2026-09-28T08:00')
    setValue(within(dialog).getByLabelText('End'), '2026-09-28T09:00')
    await user.type(within(dialog).getByLabelText('Reason'), 'Painting')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    expect(await within(dialog).findByText('End must be after Start.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('End')).toHaveAttribute('aria-invalid', 'true')
  })

  it('asks for confirmation before deleting a blackout', async () => {
    roomHandlers()
    let deleted = false
    server.use(
      http.delete(`${API}/api/rooms/1/blackouts/7`, () => {
        deleted = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/rooms/1', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: /^Delete blackout/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Delete blackout?' })
    expect(deleted).toBe(false)
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(deleted).toBe(true))
  })
})
