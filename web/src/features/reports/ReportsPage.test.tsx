import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { renderApp } from '../../test/utils'
import { campusAddDays, campusToday } from '../../ui/formatDateTime'
import { EMPTY_DEMAND } from './emptyReports'
import { reportsHandlers } from './reportsHandlers'

const region = (name: string) => screen.getByRole('region', { name })
const URL_RANGE = '/reports?from=2026-09-02&to=2026-10-31'

describe('Reports page', () => {
  it('asks for the last 30 campus days by default', async () => {
    const requests = reportsHandlers()
    renderApp('/reports', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { level: 1, name: 'Reports' })).toBeInTheDocument()
    await waitFor(() => expect(requests.utilization).toHaveLength(1))
    const today = campusToday()
    for (const params of [requests.utilization[0], requests.demand[0]]) {
      expect(params.get('from')).toBe(campusAddDays(today, -29))
      expect(params.get('to')).toBe(today)
    }
  })

  it('sends the range from the URL and a new one when a date changes', async () => {
    const requests = reportsHandlers()
    renderApp(URL_RANGE, { role: Roles.FacilitiesOfficer })

    await waitFor(() => expect(requests.demand).toHaveLength(1))
    expect(requests.demand[0].toString()).toBe('from=2026-09-02&to=2026-10-31')

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-10-01' } })
    await waitFor(() => expect(requests.demand.at(-1)?.toString()).toBe('from=2026-10-01&to=2026-10-31'))
    expect(requests.utilization.at(-1)?.toString()).toBe('from=2026-10-01&to=2026-10-31')
  })

  it('refuses a reversed or too long range without asking the API', async () => {
    const requests = reportsHandlers()
    renderApp(URL_RANGE, { role: Roles.FacilitiesOfficer })
    await waitFor(() => expect(requests.demand).toHaveLength(1))

    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-09-01' } })
    expect(await screen.findByText('To must be on or after From.')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2027-09-03' } })
    expect(await screen.findByText('A range can cover at most 366 days.')).toBeInTheDocument()
    expect(requests.demand).toHaveLength(1)
    // The last valid results stay on screen.
    expect(region('Approval rate')).toHaveTextContent('40.0% (4 of 10)')
  })

  it('shows utilization overall and per room, with denominators (captured data)', async () => {
    reportsHandlers()
    renderApp(URL_RANGE, { role: Roles.FacilitiesOfficer })

    expect(await within(await screen.findByRole('region', { name: 'Overall utilization' })).findByText('0.0% (3.0 of 11,168.0 h)'))
      .toBeInTheDocument()
    const rows = within(region('Utilization by room')).getAllByRole('row')
    // Sorted by utilization, highest first.
    expect(rows[1]).toHaveTextContent('A301')
    expect(rows[1]).toHaveTextContent('0.5% (3.0 of 588.0 h)')
  })

  it('sorts the room table on a column', async () => {
    reportsHandlers()
    const { user } = renderApp(URL_RANGE, { role: Roles.FacilitiesOfficer })

    const table = await screen.findByRole('table', { name: 'Utilization by room' })
    await user.click(within(table).getByRole('button', { name: 'Room' }))
    expect(within(table).getAllByRole('row')[1]).toHaveTextContent('A101')
    await user.click(within(table).getByRole('button', { name: 'Room' }))
    expect(within(table).getAllByRole('row')[1]).toHaveTextContent('Z51A-4')
    await user.click(within(table).getByRole('button', { name: 'Available' }))
    // Descending first: 588 h rooms, then A101 (584 h) last.
    expect(within(table).getAllByRole('row').at(-1)).toHaveTextContent('A101')
  })

  it('shows the approval rate with its denominator and the other outcomes apart', async () => {
    reportsHandlers()
    renderApp(URL_RANGE, { role: Roles.FacilitiesOfficer })

    const card = await screen.findByRole('region', { name: 'Approval rate' })
    expect(await within(card).findByText('40.0% (4 of 10)')).toBeInTheDocument()
    expect(card).toHaveTextContent('Rejected by an officer6')
    expect(card).toHaveTextContent('Closed automatically (time no longer valid)1')
    expect(card).toHaveTextContent('Cancelled before a decision4')
    expect(card).toHaveTextContent('Agent failed4')
    expect(within(region('Requests submitted')).getByText('26')).toBeInTheDocument()
  })

  it('shows the chart data as an accessible table', async () => {
    reportsHandlers()
    const { user } = renderApp(URL_RANGE, { role: Roles.FacilitiesOfficer })

    const perDay = await screen.findByRole('region', { name: 'Requests per day' })
    await user.click(within(perDay).getByRole('button', { name: 'Show as table' }))
    const table = within(perDay).getByRole('table', { name: 'Requests per day' })
    expect(within(table).getByRole('row', { name: '29 Sep 2026 13' })).toBeInTheDocument()

    const perHour = region('Requests by requested start hour')
    expect(perHour).toHaveTextContent('The most requested start hour is 14:00 (13 of 26), campus time.')
    await user.click(within(perHour).getByRole('button', { name: 'Show as table' }))
    expect(within(perHour).getByRole('row', { name: '10:00 10' })).toBeInTheDocument()
  })

  it('shows "— (0 of 0)" and empty states for a range with no requests', async () => {
    reportsHandlers({ demand: EMPTY_DEMAND })
    renderApp('/reports?from=2026-09-20&to=2026-09-20', { role: Roles.FacilitiesOfficer })

    expect(await within(await screen.findByRole('region', { name: 'Approval rate' })).findByText('— (0 of 0)')).toBeInTheDocument()
    expect(within(region('Requests per day')).getByText('No requests were submitted in this range.')).toBeInTheDocument()
  })

  it('shows an error with Retry for a report that fails', async () => {
    reportsHandlers({ status: { utilization: 500 } })
    const { user } = renderApp(URL_RANGE, { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Could not load the utilization report.')).toBeInTheDocument()
    expect(await within(await screen.findByRole('region', { name: 'Approval rate' })).findByText('40.0% (4 of 10)')).toBeInTheDocument()
    reportsHandlers()
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByRole('region', { name: 'Overall utilization' })).toBeInTheDocument()
  })

  it('sends an Admin to /forbidden and hides the nav item', async () => {
    renderApp('/reports', { role: Roles.Admin })

    expect(await screen.findByText('You do not have permission to view this page.')).toBeInTheDocument()
    const nav = screen.getAllByRole('navigation', { name: 'Main navigation' })[0]
    expect(nav).not.toHaveTextContent('Reports')
  })

  it('lists Reports in an officer’s navigation', () => {
    reportsHandlers()
    renderApp('/', { role: Roles.FacilitiesOfficer })

    const nav = screen.getAllByRole('navigation', { name: 'Main navigation' })[0]
    expect(within(nav).getByRole('link', { name: 'Reports' })).toHaveAttribute('href', '/reports')
  })
})
