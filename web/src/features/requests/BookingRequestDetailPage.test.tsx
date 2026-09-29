import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { AgentRunSummaryDto, BookingRequestDetailDto } from '../../api/types'
import { Roles } from '../../auth/roles'
import { BOOKING_REQUEST_DETAILS } from '../../test/fixtures'
import { PENDING_DETAIL, PENDING_RUNS, RETRY_NOT_RESTARTABLE } from '../approvals/approvalsFixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { useToastStore } from '../../ui/toastStore'
import { requestsHandlers } from './requestsHandlers'

const card = (name: string) => screen.getByRole('region', { name })

describe('BookingRequestDetailPage', () => {
  it('shows the header and every card of a club request', async () => {
    requestsHandlers()
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { level: 1, name: 'Robotics Club Arduino workshop' })).toBeInTheDocument()
    expect(screen.getAllByText('Waiting for approval').length).toBeGreaterThan(0)
    expect(screen.getByRole('link', { name: 'Back to requests' })).toHaveAttribute('href', '/requests')

    const requester = card('Requester')
    expect(within(requester).getByText('Kavindi Perera')).toBeInTheDocument()
    expect(within(requester).getByText('kavindi@campusspace.local')).toBeInTheDocument()
    expect(within(requester).getByText('Robotics Club')).toBeInTheDocument()

    const booking = card('Booking')
    expect(within(booking).getByText('Tue 20 Oct 2026, 14:00–17:00')).toBeInTheDocument()
    expect(within(booking).getByText('40')).toBeInTheDocument()
    expect(within(booking).getByText('LKR 12,500.50')).toBeInTheDocument()

    expect(within(card('Required features')).getByText('Projector')).toBeInTheDocument()
    const equipment = card('Equipment')
    expect(within(equipment).getByText('MIC-WIRELESS — Wireless microphone × 1')).toBeInTheDocument()
    expect(within(equipment).getByText('PROJ-PORTABLE — Portable projector × 2')).toBeInTheDocument()

    expect(
      within(card('Agent proposal')).getByText(
        'No proposal yet. The proposal, validation checklist and agent trace appear once the agents have planned.',
      ),
    ).toBeInTheDocument()
    expect(await within(card('Agent runs')).findByText('No agent runs yet')).toBeInTheDocument()
  })

  it('shows the requester notes as plain text with the line break kept', async () => {
    requestsHandlers()
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    const notes = within(await screen.findByRole('region', { name: 'Requester notes (as written by the requester)' }))
      .getByTestId('request-notes')
    expect(notes.textContent).toBe('Please keep <b>bold</b> as typed.\nWe need extension cords.')
    // Markup stays text: no element was created from it.
    expect(notes.querySelector('b')).toBeNull()
    expect(notes.children).toHaveLength(0)
    expect(notes).toHaveStyle({ whiteSpace: 'pre-wrap' })
  })

  it('lists the status history oldest first, with System for automatic changes', async () => {
    requestsHandlers()
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    const items = within(await screen.findByRole('list', { name: 'Status timeline' })).getAllByRole('listitem')
    expect(items.map((i) => within(i).getByText(/^(Submitted|Processing|Waiting for approval)$/).textContent)).toEqual([
      'Submitted',
      'Processing',
      'Waiting for approval',
    ])
    expect(items[0]).toHaveTextContent('Kavindi Perera')
    expect(items[0]).toHaveTextContent('27 Sept 2026, 14:30')
    expect(items[1]).toHaveTextContent('System')
    expect(items[2]).toHaveTextContent('System')
    expect(items[2]).toHaveTextContent('Proposal ready for review')
  })

  it('shows an academic booking with no notes', async () => {
    requestsHandlers()
    renderApp('/requests/2', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { level: 1, name: 'Guest lecture: AI in agriculture' })).toBeInTheDocument()
    expect(within(card('Requester')).getByText('Academic booking')).toBeInTheDocument()
    expect(within(card('Booking')).getByText('LKR 0.00')).toBeInTheDocument()
    expect(within(card('Requester notes (as written by the requester)')).getByText('None')).toBeInTheDocument()
  })

  it.each(['/requests/999', '/requests/403', '/requests/abc'])('shows "Request not found" for %s', async (route) => {
    requestsHandlers()
    renderApp(route, { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText(/Request not found/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to requests' })).toHaveAttribute('href', '/requests')
  })

  it('offers Retry on a server error', async () => {
    requestsHandlers()
    server.use(
      http.get(`${API}/api/booking-requests/:id`, () =>
        HttpResponse.json({ title: 'Server error', status: 500 }, { status: 500 }),
      ),
    )
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText(/Could not load the request/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  describe('cancellation', () => {
    const base = BOOKING_REQUEST_DETAILS[2]

    /** Serves request 3 with the given overrides (and records cancel bodies) on top of the usual handlers. */
    function serveRequest(overrides: Partial<BookingRequestDetailDto>) {
      requestsHandlers()
      const bodies: unknown[] = []
      server.use(
        http.get(`${API}/api/booking-requests/3`, () => HttpResponse.json({ ...base, ...overrides })),
        http.post(`${API}/api/booking-requests/3/cancel`, async ({ request }) => {
          bodies.push(await request.json())
          return HttpResponse.json({ ...base, status: 'Cancelled' })
        }),
      )
      return bodies
    }

    const cancelledBy = (changedByName: string, reason: string | null) => ({
      status: 'Cancelled',
      cancelledAt: '2026-10-19T04:30:00Z',
      history: [
        ...base.history,
        { fromStatus: 'PendingApproval', toStatus: 'Cancelled', changedById: 4, changedByName, reason, changedAt: '2026-10-19T04:30:00Z' },
      ],
    })

    it('shows an officer cancellation with its reason as plain text, and no cancel action', async () => {
      serveRequest({ ...cancelledBy('Mr. Perera', 'Room unavailable: <i>Rewiring</i>\nSorry'), cancelledByOfficer: true })
      renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      const cancellation = await screen.findByRole('region', { name: 'Cancellation' })
      // 04:30Z is 10:00 in Colombo.
      expect(within(cancellation).getByText(/^Cancelled on .*10:00/)).toBeInTheDocument()
      expect(within(cancellation).getByText('Cancelled by the facilities office')).toBeInTheDocument()
      expect(within(cancellation).queryByText('Late cancellation')).not.toBeInTheDocument()
      expect(within(cancellation).getByTestId('cancel-reason').textContent).toBe('Room unavailable: <i>Rewiring</i>\nSorry')
      expect(within(cancellation).queryByRole('emphasis')).toBeNull()
      expect(screen.queryByRole('button', { name: 'Cancel request' })).not.toBeInTheDocument()
    })

    it("shows an owner's late cancellation without a reason", async () => {
      serveRequest({ ...cancelledBy('Kavindi Perera', null), isLateCancellation: true })
      renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      const cancellation = await screen.findByRole('region', { name: 'Cancellation' })
      expect(within(cancellation).getByText('Late cancellation')).toBeInTheDocument()
      expect(within(cancellation).queryByText('Cancelled by the facilities office')).not.toBeInTheDocument()
      expect(within(cancellation).getByText('No reason given')).toBeInTheDocument()
    })

    it('has no cancellation card for a request that is not cancelled', async () => {
      serveRequest({})
      renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await screen.findByRole('heading', { level: 1 })
      expect(screen.queryByRole('region', { name: 'Cancellation' })).not.toBeInTheDocument()
    })

    it.each([
      ['Submitted', true],
      ['PendingApproval', true],
      ['Approved', true],
      ['AgentProcessing', false],
      ['RevisionRequested', false],
      ['Completed', false],
      ['Rejected', false],
    ])('offers Cancel request for %s: %s', async (status, offered) => {
      serveRequest({ status })
      renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await screen.findByRole('heading', { level: 1 })
      expect(!!screen.queryByRole('button', { name: 'Cancel request' })).toBe(offered)
    })

    it('requires a reason, then cancels with it and reloads the request', async () => {
      const bodies = serveRequest({ status: 'Approved' })
      let reads = 0
      server.use(
        http.get(`${API}/api/booking-requests/3`, () => {
          reads++
          return HttpResponse.json({ ...base, status: 'Approved' })
        }),
      )
      const { user } = renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: 'Cancel request' }))
      const dialog = await screen.findByRole('dialog', { name: 'Cancel request?' })
      await user.click(within(dialog).getByRole('button', { name: 'Cancel request' }))
      expect(await within(dialog).findByText('A reason is required')).toBeInTheDocument()
      await user.type(within(dialog).getByLabelText(/Reason/), '   ')
      await user.click(within(dialog).getByRole('button', { name: 'Cancel request' }))
      expect(await within(dialog).findByText('A reason is required')).toBeInTheDocument()
      expect(bodies).toHaveLength(0)

      await user.clear(within(dialog).getByLabelText(/Reason/))
      await user.type(within(dialog).getByLabelText(/Reason/), 'Exam scheduled')
      await user.click(within(dialog).getByRole('button', { name: 'Cancel request' }))

      await waitFor(() => expect(bodies).toEqual([{ reason: 'Exam scheduled' }]))
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      await waitFor(() => expect(reads).toBeGreaterThan(1))
      expect(useToastStore.getState().current).toMatchObject({ severity: 'success', message: 'Request cancelled' })
    })

    it('closes with Keep request and sends nothing', async () => {
      const bodies = serveRequest({ status: 'Submitted' })
      const { user } = renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: 'Cancel request' }))
      const dialog = await screen.findByRole('dialog', { name: 'Cancel request?' })
      await user.click(within(dialog).getByRole('button', { name: 'Keep request' }))

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(bodies).toHaveLength(0)
    })

    it('shows a 409 title exactly as sent', async () => {
      serveRequest({ status: 'Approved' })
      server.use(
        http.post(`${API}/api/booking-requests/3/cancel`, () =>
          HttpResponse.json({ status: 409, title: 'Equipment is still on loan; check it in first' }, { status: 409 }),
        ),
      )
      const { user } = renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: 'Cancel request' }))
      const dialog = await screen.findByRole('dialog', { name: 'Cancel request?' })
      await user.type(within(dialog).getByLabelText(/Reason/), 'Exam scheduled')
      await user.click(within(dialog).getByRole('button', { name: 'Cancel request' }))

      await waitFor(() =>
        expect(useToastStore.getState().current).toMatchObject({
          severity: 'error',
          message: 'Equipment is still on loan; check it in first',
        }),
      )
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    })
  })

  it('re-fetches an AgentProcessing request until the agent moves it to PendingApproval', async () => {
    requestsHandlers()
    const base = BOOKING_REQUEST_DETAILS.find((r) => r.id === 3)!
    let calls = 0
    server.use(
      http.get(`${API}/api/booking-requests/3`, () => {
        calls++
        return HttpResponse.json({ ...base, status: calls === 1 ? 'AgentProcessing' : 'PendingApproval' })
      }),
    )
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    // The history may show both labels too, so compare counts: the status chip moves from one to the other.
    const processing = (await screen.findAllByText('Processing')).length
    const waiting = screen.queryAllByText('Waiting for approval').length
    await waitFor(() => expect(screen.queryAllByText('Processing')).toHaveLength(processing - 1), { timeout: 5000 })
    expect(screen.getAllByText('Waiting for approval')).toHaveLength(waiting + 1)
    expect(calls).toBe(2)
  }, 10_000)

  describe('agent actions', () => {
    const base = BOOKING_REQUEST_DETAILS[2]
    const failedRun: AgentRunSummaryDto = {
      ...PENDING_RUNS[0],
      status: 'Failed',
      failureReason: 'Agent service unreachable',
      completedAt: null,
    }

    function serve(overrides: Partial<BookingRequestDetailDto>, runs: AgentRunSummaryDto[] = []) {
      requestsHandlers({ runs })
      server.use(http.get(`${API}/api/booking-requests/3`, () => HttpResponse.json({ ...base, ...overrides })))
    }

    it('links a PendingApproval request to its approval screen and shows the proposal summary', async () => {
      serve({ latestProposal: PENDING_DETAIL.latestProposal }, PENDING_RUNS)
      renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      expect(await screen.findByRole('link', { name: 'Review proposal' })).toHaveAttribute('href', '/approvals/3')
      const proposal = card('Agent proposal')
      expect(within(proposal).getByText('A301 · Computer Lab A301')).toBeInTheDocument()
      expect(within(proposal).getByText('LKR 5,500.00')).toBeInTheDocument()
      expect(within(proposal).getByText('Draft quote')).toBeInTheDocument()
      expect(await within(card('Agent runs')).findByText('AwaitingApproval')).toBeInTheDocument()
    })

    it.each([
      ['AgentFailed', [failedRun], 'Retry agent'],
      ['Submitted', [], 'Start agent'],
      ['Submitted', [failedRun], 'Start agent'],
      ['Submitted', PENDING_RUNS, null],
      ['PendingApproval', PENDING_RUNS, null],
      ['Approved', PENDING_RUNS, null],
    ] as const)('for %s with those runs offers %s', async (status, runs, label) => {
      serve({ status }, [...runs])
      renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await screen.findByRole('heading', { level: 1 })
      // The button waits for the runs (Start agent needs to know there is no live run).
      await waitFor(() => expect(within(card('Agent runs')).queryByLabelText('Loading agent runs')).toBeNull())
      for (const name of ['Retry agent', 'Start agent'])
        expect(!!screen.queryByRole('button', { name })).toBe(name === label)
    })

    it('retries a failed request: 202 shows a toast and reloads', async () => {
      serve({ status: 'AgentFailed' }, [failedRun])
      let posts = 0
      server.use(
        http.post(`${API}/api/booking-requests/3/retry-agent`, () => {
          posts++
          return HttpResponse.json({ ...base, status: 'AgentProcessing' }, { status: 202 })
        }),
      )
      const { user } = renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: 'Retry agent' }))

      await waitFor(() =>
        expect(useToastStore.getState().current).toMatchObject({
          severity: 'success',
          message: 'Agent started: a new proposal is being prepared',
        }),
      )
      expect(posts).toBe(1)
    })

    it('shows a retry-agent 409 exactly as sent', async () => {
      serve({ status: 'AgentFailed' }, [failedRun])
      server.use(
        http.post(`${API}/api/booking-requests/3/retry-agent`, () =>
          HttpResponse.json(RETRY_NOT_RESTARTABLE, { status: 409 }),
        ),
      )
      const { user } = renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: 'Retry agent' }))

      await waitFor(() =>
        expect(useToastStore.getState().current).toMatchObject({
          severity: 'error',
          message: 'Only a failed or not-yet-started request can be (re)started',
        }),
      )
    })

    it('shows the requester-cap 409 exactly as sent', async () => {
      serve({ status: 'AgentFailed' }, [failedRun])
      const title = 'The requester already has 3 open requests (the limit is 3)'
      server.use(
        http.post(`${API}/api/booking-requests/3/retry-agent`, () =>
          HttpResponse.json({ ...RETRY_NOT_RESTARTABLE, title }, { status: 409 }),
        ),
      )
      const { user } = renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

      await user.click(await screen.findByRole('button', { name: 'Retry agent' }))

      await waitFor(() => expect(useToastStore.getState().current).toMatchObject({ severity: 'error', message: title }))
    })
  })
})
