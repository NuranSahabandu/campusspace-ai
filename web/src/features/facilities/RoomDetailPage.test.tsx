import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import type { BlackoutClashDto } from '../../api/types'
import { BLACKOUTS, BUILDINGS, FEATURES, makeRoom, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { useToastStore } from '../../ui/toastStore'

const CLASHES: BlackoutClashDto[] = [
  {
    bookingId: 31,
    requestId: 12,
    start: '2026-09-28T03:30:00Z',
    end: '2026-09-28T05:30:00Z',
    status: 'Confirmed',
    requesterName: 'Kavindi Perera',
    requesterEmail: 'kavindi@campusspace.local',
  },
  {
    bookingId: 32,
    requestId: 14,
    start: '2026-09-28T05:30:00Z',
    end: '2026-09-28T06:30:00Z',
    status: 'CheckedIn',
    requesterName: 'Dr. Nimal Fernando',
    requesterEmail: 'lecturer@campusspace.local',
  },
]

/**
 * Blackout 7 with clashes: the clashes endpoint drops a request once it is cancelled. Returns the recorded cancel
 * calls and how often the clashes and blackouts were read.
 */
function clashHandlers() {
  const blackoutReads = roomHandlers()
  const cancelled = new Set<number>()
  const cancels: { id: number; body: unknown }[] = []
  let clashReads = 0
  const live = () => CLASHES.filter((c) => !cancelled.has(c.requestId))
  server.use(
    http.get(`${API}/api/rooms/1/blackouts`, ({ request }) => {
      blackoutReads.push(new URL(request.url).searchParams)
      return HttpResponse.json(pageOf([{ ...BLACKOUTS[0], reason: 'Rewiring', clashCount: live().length }]))
    }),
    http.get(`${API}/api/rooms/1/blackouts/7/clashes`, () => {
      clashReads++
      return HttpResponse.json(live())
    }),
    http.post(`${API}/api/booking-requests/:id/cancel`, async ({ params, request }) => {
      const id = Number(params.id)
      cancels.push({ id, body: await request.json() })
      cancelled.add(id)
      return HttpResponse.json({})
    }),
  )
  return { cancels, blackoutReads, clashReads: () => clashReads }
}

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
    expect(screen.getByText(/Adding a blackout lists the active bookings it clashes with. They are not cancelled automatically/)).toBeInTheDocument()
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
        return HttpResponse.json({ ...BLACKOUTS[0], clashes: [] }, { status: 201 })
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

  it('keeps the dialog open with a warning listing the clashing bookings, and refreshes the list behind it', async () => {
    const requests = roomHandlers()
    server.use(
      http.post(`${API}/api/rooms/1/blackouts`, () =>
        HttpResponse.json(
          {
            ...BLACKOUTS[0],
            clashes: [
              {
                bookingId: 31,
                requestId: 12,
                start: '2026-09-28T03:30:00Z',
                end: '2026-09-28T05:30:00Z',
                status: 'Confirmed',
                requesterName: '<b>Kavindi</b>',
                requesterEmail: 'kavindi@campusspace.local',
              },
            ],
          },
          { status: 201 },
        ),
      ),
    )
    const { user, dialog } = await openAddBlackout()
    await waitFor(() => expect(requests).toHaveLength(1))

    setValue(within(dialog).getByLabelText('Start'), '2026-09-28T08:00')
    setValue(within(dialog).getByLabelText('End'), '2026-09-28T12:00')
    await user.type(within(dialog).getByLabelText('Reason'), 'Painting')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    const open = await screen.findByRole('dialog', { name: 'Add blackout to A301' })
    expect(
      await within(open).findByText('Blackout added. It clashes with 1 active booking; they were not cancelled.'),
    ).toBeInTheDocument()
    const list = within(open).getByRole('list', { name: 'Clashing bookings' })
    // 03:30Z–05:30Z is 09:00–11:00 in Colombo. The name is shown as text, not rendered as HTML.
    expect(within(list).getByText(/09:00–11:00 · Confirmed/)).toBeInTheDocument()
    expect(within(list).getByText('<b>Kavindi</b> (kavindi@campusspace.local) ·')).toBeInTheDocument()
    expect(within(list).getByRole('link', { name: 'request #12' })).toHaveAttribute('href', '/requests/12')
    await waitFor(() => expect(requests.length).toBeGreaterThan(1))

    await user.click(within(open).getByRole('button', { name: 'Close' }))
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

  describe('clashes', () => {
    it("shows each blackout's clash count and opens its clash list", async () => {
      clashHandlers()
      const { user } = renderApp('/rooms/1', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: /^Show 2 clashing bookings/ }))
      const dialog = await screen.findByRole('dialog', { name: 'Clashing bookings' })
      expect(within(dialog).getByText(/Rewiring/)).toBeInTheDocument()
      const list = await within(dialog).findByRole('list', { name: 'Clashing bookings' })
      // 03:30Z–05:30Z is 09:00–11:00 in Colombo.
      expect(within(list).getByText(/09:00–11:00 · Confirmed/)).toBeInTheDocument()
      expect(within(list).getByText('Kavindi Perera (kavindi@campusspace.local) ·')).toBeInTheDocument()
      expect(within(list).getByText(/11:00–12:00 · CheckedIn/)).toBeInTheDocument()
      expect(within(list).getAllByRole('button', { name: /^Cancel booking/ })).toHaveLength(2)
    })

    it('shows "None" for a blackout without clashes', async () => {
      roomHandlers()
      renderApp('/rooms/1', { role: Roles.FacilitiesOfficer })

      expect(await screen.findByText('None')).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /clashing booking/ })).not.toBeInTheDocument()
    })

    it('cancels a clash with a required, prefilled reason and refreshes the clashes and blackouts', async () => {
      const { cancels, blackoutReads, clashReads } = clashHandlers()
      const { user } = renderApp('/rooms/1', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: /^Show 2 clashing bookings/ }))
      const clashes = await screen.findByRole('dialog', { name: 'Clashing bookings' })
      await user.click(await within(clashes).findByRole('button', { name: /^Cancel booking .* for Kavindi Perera/ }))

      const confirm = await screen.findByRole('dialog', { name: 'Cancel request?' })
      expect(within(confirm).getByText(/request #12 by Kavindi Perera/)).toBeInTheDocument()
      const reason = within(confirm).getByLabelText(/Reason/)
      expect(reason).toHaveValue('Room unavailable: Rewiring')

      // A blank reason is blocked before anything is sent.
      await user.clear(reason)
      await user.click(within(confirm).getByRole('button', { name: 'Cancel request' }))
      expect(await within(confirm).findByText('A reason is required')).toBeInTheDocument()
      expect(cancels).toHaveLength(0)

      await user.type(reason, 'Rewiring overran')
      const readsBefore = { clashes: clashReads(), blackouts: blackoutReads.length }
      await user.click(within(confirm).getByRole('button', { name: 'Cancel request' }))

      await waitFor(() => expect(cancels).toEqual([{ id: 12, body: { reason: 'Rewiring overran' } }]))
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Cancel request?' })).not.toBeInTheDocument())
      await waitFor(() => expect(within(clashes).queryByText(/Kavindi Perera/)).not.toBeInTheDocument())
      expect(within(clashes).getByText(/Dr. Nimal Fernando/)).toBeInTheDocument()
      expect(clashReads()).toBeGreaterThan(readsBefore.clashes)
      expect(blackoutReads.length).toBeGreaterThan(readsBefore.blackouts)
      // The row behind the dialog now counts one clash.
      await user.click(within(clashes).getByRole('button', { name: 'Close' }))
      expect(await screen.findByRole('button', { name: /^Show 1 clashing booking for/ })).toBeInTheDocument()
    })

    it('shows a 409 title exactly as sent and reloads the clashes', async () => {
      const { clashReads } = clashHandlers()
      server.use(
        http.post(`${API}/api/booking-requests/:id/cancel`, () =>
          HttpResponse.json({ status: 409, title: 'The booking has already started' }, { status: 409 }),
        ),
      )
      const { user } = renderApp('/rooms/1', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: /^Show 2 clashing bookings/ }))
      const clashes = await screen.findByRole('dialog', { name: 'Clashing bookings' })
      await user.click(await within(clashes).findByRole('button', { name: /^Cancel booking .* for Kavindi Perera/ }))
      const confirm = await screen.findByRole('dialog', { name: 'Cancel request?' })
      const readsBefore = clashReads()
      await user.click(within(confirm).getByRole('button', { name: 'Cancel request' }))

      await waitFor(() =>
        expect(useToastStore.getState().current).toMatchObject({ severity: 'error', message: 'The booking has already started' }),
      )
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Cancel request?' })).not.toBeInTheDocument())
      await waitFor(() => expect(clashReads()).toBeGreaterThan(readsBefore))
    })

    it('cancels from the add-blackout warning list, which then refreshes', async () => {
      const { cancels } = clashHandlers()
      server.use(
        http.post(`${API}/api/rooms/1/blackouts`, () =>
          HttpResponse.json({ ...BLACKOUTS[0], reason: 'Rewiring', clashCount: 2, clashes: CLASHES }, { status: 201 }),
        ),
      )
      const { user, dialog } = await openAddBlackout()
      setValue(within(dialog).getByLabelText('Start'), '2026-09-28T08:00')
      setValue(within(dialog).getByLabelText('End'), '2026-09-28T12:00')
      await user.type(within(dialog).getByLabelText('Reason'), 'Rewiring')
      await user.click(within(dialog).getByRole('button', { name: 'Add' }))

      const warning = await screen.findByRole('dialog', { name: 'Add blackout to A301' })
      expect(await within(warning).findByText(/It clashes with 2 active bookings/)).toBeInTheDocument()
      await user.click(within(warning).getByRole('button', { name: /^Cancel booking .* for Dr. Nimal Fernando/ }))
      const confirm = await screen.findByRole('dialog', { name: 'Cancel request?' })
      expect(within(confirm).getByLabelText(/Reason/)).toHaveValue('Room unavailable: Rewiring')
      await user.click(within(confirm).getByRole('button', { name: 'Cancel request' }))

      await waitFor(() => expect(cancels).toEqual([{ id: 14, body: { reason: 'Room unavailable: Rewiring' } }]))
      expect(await within(warning).findByText(/It clashes with 1 active booking;/)).toBeInTheDocument()
      expect(within(warning).queryByText(/Dr. Nimal Fernando/)).not.toBeInTheDocument()
    })
  })
})
