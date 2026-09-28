import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { BOOKING_REQUESTS, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { requestsHandlers } from './requestsHandlers'

const rowOf = (purpose: string) => screen.getByText(purpose).closest('[role="row"]') as HTMLElement
const groupButton = (name: string) =>
  within(screen.getByRole('group', { name: 'Status group' })).getByRole('button', { name })
const OPEN = ['Submitted', 'AgentProcessing', 'PendingApproval', 'RevisionRequested']

/** Lets any request the last render would have started reach the MSW handler. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 150))

describe('BookingRequestsPage', () => {
  it('lists requests with requester, club, when, budget and status, Open and newest first by default', async () => {
    const { listRequests } = requestsHandlers()
    renderApp('/requests', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Robotics Club Arduino workshop')).toBeInTheDocument()
    expect(listRequests[0].getAll('status')).toEqual(OPEN)
    expect(listRequests[0].get('sort')).toBe('-createdAt')
    expect(groupButton('Open')).toHaveAttribute('aria-pressed', 'true')

    const workshop = rowOf('Robotics Club Arduino workshop')
    expect(within(workshop).getByText('Kavindi Perera')).toBeInTheDocument()
    expect(within(workshop).getByText('kavindi@campusspace.local')).toBeInTheDocument()
    expect(within(workshop).getByText('Robotics Club')).toBeInTheDocument()
    expect(within(workshop).getByText('Tue 20 Oct 2026, 14:00–17:00')).toBeInTheDocument()
    expect(within(workshop).getByText('40')).toBeInTheDocument()
    expect(within(workshop).getByText('LKR 12,500.50')).toBeInTheDocument()
    expect(within(workshop).getByText('Waiting for approval')).toBeInTheDocument()

    const lecture = rowOf('Guest lecture: AI in agriculture')
    expect(within(lecture).getByText('Dr. Nimal Fernando')).toBeInTheDocument()
    expect(within(lecture).getByText('Academic')).toBeInTheDocument()
    expect(within(lecture).getByText('LKR 0.00')).toBeInTheDocument()
    expect(within(lecture).getByText('Submitted')).toBeInTheDocument()

    expect(within(rowOf('Drama Society rehearsal')).getByText('Drama Society')).toBeInTheDocument()
  })

  it('sends each quick filter chip as its group of statuses', async () => {
    const { listRequests } = requestsHandlers()
    const { user } = renderApp('/requests', { role: Roles.FacilitiesOfficer })
    await screen.findByText('Robotics Club Arduino workshop')

    await user.click(groupButton('Approved'))
    await waitFor(() => expect(listRequests.at(-1)?.getAll('status')).toEqual(['Approved', 'Completed']))

    await user.click(groupButton('Closed'))
    await waitFor(() => expect(listRequests.at(-1)?.getAll('status')).toEqual(['Rejected', 'Cancelled', 'AgentFailed']))

    await user.click(groupButton('All'))
    await waitFor(() => expect(listRequests.at(-1)?.has('status')).toBe(false))
    expect(groupButton('All')).toHaveAttribute('aria-pressed', 'true')

    // Back to Open: the same query as the first one, answered from the cache.
    await user.click(groupButton('Open'))
    expect(groupButton('Open')).toHaveAttribute('aria-pressed', 'true')
    expect(await screen.findByText('Robotics Club Arduino workshop')).toBeInTheDocument()
  })

  it('lets an exact status override the group', async () => {
    const { listRequests } = requestsHandlers()
    const { user } = renderApp('/requests', { role: Roles.FacilitiesOfficer })
    await screen.findByText('Robotics Club Arduino workshop')

    await user.click(screen.getByRole('combobox', { name: 'Exact status' }))
    await user.click(await screen.findByRole('option', { name: 'Failed' }))
    await user.click(await screen.findByRole('option', { name: 'Needs revision' }))
    await waitFor(() => expect(listRequests.at(-1)?.getAll('status')).toEqual(['AgentFailed', 'RevisionRequested']))
    await user.keyboard('{Escape}')

    // No group is selected while an exact status is; picking a group again clears the exact choice.
    for (const name of ['All', 'Open', 'Approved', 'Closed'])
      expect(groupButton(name)).toHaveAttribute('aria-pressed', 'false')
    await user.click(groupButton('Closed'))
    await waitFor(() => expect(listRequests.at(-1)?.getAll('status')).toEqual(['Rejected', 'Cancelled', 'AgentFailed']))
    expect(screen.getByRole('combobox', { name: 'Exact status' })).not.toHaveTextContent(/Failed|revision/)
  })

  it('sends search, the date range and the club', async () => {
    const { listRequests } = requestsHandlers()
    const { user } = renderApp('/requests', { role: Roles.FacilitiesOfficer })
    await screen.findByText('Robotics Club Arduino workshop')

    await user.type(screen.getByLabelText('Purpose, requester or club'), 'Kavindi')
    await waitFor(() => expect(listRequests.at(-1)?.get('search')).toBe('Kavindi'))

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-10-01' } })
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-10-31' } })
    await waitFor(() => expect(listRequests.at(-1)?.get('to')).toBe('2026-10-31'))
    expect(listRequests.at(-1)?.get('from')).toBe('2026-10-01')

    await user.click(screen.getByRole('combobox', { name: 'Club' }))
    await user.click(await screen.findByRole('option', { name: 'Robotics Club' }))
    await waitFor(() => expect(listRequests.at(-1)?.get('clubId')).toBe('1'))

    const last = listRequests.at(-1)!
    expect(last.get('search')).toBe('Kavindi')
    expect(last.getAll('status')).toEqual(OPEN)
    expect(last.get('page')).toBe('1')
  })

  it('shows a client error for To before From, sends nothing and keeps the loaded rows', async () => {
    const { listRequests } = requestsHandlers()
    renderApp('/requests', { role: Roles.FacilitiesOfficer })
    await screen.findByText('Robotics Club Arduino workshop')

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-10-10' } })
    await waitFor(() => expect(listRequests.at(-1)?.get('from')).toBe('2026-10-10'))
    const sent = listRequests.length

    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-10-01' } })
    expect(await screen.findByText('To must be on or after From')).toBeInTheDocument()
    await settle()

    expect(listRequests).toHaveLength(sent)
    expect(listRequests.some((p) => p.get('to') === '2026-10-01')).toBe(false)
    expect(screen.queryByLabelText('Loading booking requests')).not.toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
    expect(screen.getByText('Robotics Club Arduino workshop')).toBeInTheDocument()

    // Fixing the range sends it.
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-10-20' } })
    await waitFor(() => expect(listRequests.at(-1)?.get('to')).toBe('2026-10-20'))
    expect(screen.queryByText('To must be on or after From')).not.toBeInTheDocument()
  })

  it('shows an empty grid, not a loader, when the page opens with To before From', async () => {
    const { listRequests } = requestsHandlers()
    renderApp('/requests?from=2026-10-10&to=2026-10-01', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('To must be on or after From')).toBeInTheDocument()
    // The grid's empty overlay re-mounts as it renders, so look it up again rather than holding the element.
    await waitFor(() => expect(screen.getByText('No requests match these filters')).toBeInTheDocument())
    await settle()
    expect(listRequests).toHaveLength(0)
    expect(screen.queryByLabelText('Loading booking requests')).not.toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
  })

  it('maps column sorting to the server sort fields', async () => {
    const { listRequests } = requestsHandlers()
    const { user } = renderApp('/requests', { role: Roles.FacilitiesOfficer })
    await screen.findByText('Robotics Club Arduino workshop')

    await user.click(screen.getByRole('columnheader', { name: /^When/ }))
    await waitFor(() => expect(listRequests.at(-1)?.get('sort')).toBe('requestedStart'))
    await user.click(screen.getByRole('columnheader', { name: /^When/ }))
    await waitFor(() => expect(listRequests.at(-1)?.get('sort')).toBe('-requestedStart'))

    await user.click(screen.getByRole('columnheader', { name: /^Attendees/ }))
    await waitFor(() => expect(listRequests.at(-1)?.get('sort')).toBe('attendees'))

    await user.click(screen.getByRole('columnheader', { name: /^Status/ }))
    await waitFor(() => expect(listRequests.at(-1)?.get('sort')).toBe('status'))

    await user.click(screen.getByRole('columnheader', { name: /^Submitted/ }))
    await waitFor(() => expect(listRequests.at(-1)?.get('sort')).toBe('createdAt'))
  })

  it('marks cancelled rows that were late or cancelled by the office', async () => {
    const [workshop, lecture, rehearsal] = BOOKING_REQUESTS
    requestsHandlers({
      rows: [
        { ...workshop, status: 'Cancelled', cancelledAt: '2026-10-19T04:30:00Z', cancelledByOfficer: true },
        { ...lecture, status: 'Cancelled', cancelledAt: '2026-10-25T04:30:00Z', isLateCancellation: true },
        rehearsal,
      ],
    })
    renderApp('/requests', { role: Roles.FacilitiesOfficer })

    await screen.findByText('Robotics Club Arduino workshop')
    const byOffice = rowOf('Robotics Club Arduino workshop')
    expect(within(byOffice).getByText('By office')).toBeInTheDocument()
    expect(within(byOffice).queryByText('Late')).not.toBeInTheDocument()
    const late = rowOf('Guest lecture: AI in agriculture')
    expect(within(late).getByText('Late')).toBeInTheDocument()
    expect(within(late).queryByText('By office')).not.toBeInTheDocument()
    const open = rowOf('Drama Society rehearsal')
    expect(within(open).queryByText('Late')).not.toBeInTheDocument()
    expect(within(open).queryByText('By office')).not.toBeInTheDocument()
  })

  it('shows the empty state', async () => {
    requestsHandlers({ rows: [] })
    renderApp('/requests', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('No requests match these filters')).toBeInTheDocument()
  })

  it('opens a request by clicking its row or its View action', async () => {
    requestsHandlers()
    const { user } = renderApp('/requests', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByText('Robotics Club Arduino workshop'))
    expect(await screen.findByRole('heading', { level: 1, name: 'Robotics Club Arduino workshop' })).toBeInTheDocument()

    await user.click(screen.getByRole('link', { name: 'Back to requests' }))
    await user.click(await screen.findByRole('button', { name: 'View Guest lecture: AI in agriculture' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Guest lecture: AI in agriculture' })).toBeInTheDocument()
  })

  it('keeps the filters, search, sort and page when coming back from a request', async () => {
    const { listRequests } = requestsHandlers()
    const { user } = renderApp('/requests', { role: Roles.FacilitiesOfficer })
    await screen.findByText('Robotics Club Arduino workshop')

    await user.click(groupButton('All'))
    await user.type(screen.getByLabelText('Purpose, requester or club'), 'Kavindi')
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-10-01' } })
    await user.click(screen.getByRole('combobox', { name: 'Club' }))
    await user.click(await screen.findByRole('option', { name: 'Robotics Club' }))
    await user.click(screen.getByRole('columnheader', { name: /^When/ }))
    await waitFor(() => expect(listRequests.at(-1)?.get('sort')).toBe('requestedStart'))
    await waitFor(() => expect(listRequests.at(-1)?.get('search')).toBe('Kavindi'))
    const before = listRequests.at(-1)!.toString()

    await user.click(screen.getByText('Robotics Club Arduino workshop'))
    await user.click(await screen.findByRole('link', { name: 'Back to requests' }))

    expect(await screen.findByText('Robotics Club Arduino workshop')).toBeInTheDocument()
    expect(groupButton('All')).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByLabelText('Purpose, requester or club')).toHaveValue('Kavindi')
    expect(screen.getByLabelText('From')).toHaveValue('2026-10-01')
    expect(screen.getByRole('combobox', { name: 'Club' })).toHaveTextContent('Robotics Club')
    expect(screen.getByRole('columnheader', { name: /^When/ })).toHaveAttribute('aria-sort', 'ascending')
    await waitFor(() => expect(listRequests.at(-1)?.toString()).toBe(before))
  })

  it('ignores invalid filter values in the URL', async () => {
    const { listRequests } = requestsHandlers()
    renderApp('/requests?group=nope&status=Bogus&from=soon&clubId=abc&sort=purpose&page=0', {
      role: Roles.FacilitiesOfficer,
    })

    await screen.findByText('Robotics Club Arduino workshop')
    const first = listRequests[0]
    expect(first.getAll('status')).toEqual(OPEN)
    expect(first.has('from')).toBe(false)
    expect(first.has('clubId')).toBe(false)
    expect(first.get('sort')).toBe('-createdAt')
    expect(first.get('page')).toBe('1')
  })

  it('re-fetches the list while a row is AgentProcessing and stops once none is', async () => {
    requestsHandlers()
    let calls = 0
    server.use(
      http.get(`${API}/api/booking-requests`, () => {
        calls++
        const status = calls === 1 ? 'AgentProcessing' : 'PendingApproval'
        return HttpResponse.json(pageOf(BOOKING_REQUESTS.map((r, i) => (i === 0 ? { ...r, status } : r))))
      }),
    )
    renderApp('/requests', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Processing')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByText('Processing')).not.toBeInTheDocument(), { timeout: 5000 })
    await new Promise((resolve) => setTimeout(resolve, 3500))
    expect(calls).toBe(2)
  }, 15_000)
})
