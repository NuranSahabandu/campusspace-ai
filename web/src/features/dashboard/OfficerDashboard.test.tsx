import { screen, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { EMPTY_DASHBOARD } from '../reports/emptyReports'
import { reportsHandlers } from '../reports/reportsHandlers'

const card = (title: string) => screen.getByRole('region', { name: title })

describe('Officer dashboard', () => {
  it('shows every KPI with its denominator (captured data)', async () => {
    reportsHandlers()
    renderApp('/', { role: Roles.FacilitiesOfficer })

    expect(await within(await screen.findByRole('region', { name: 'Pending approvals' })).findByText('4')).toBeInTheDocument()
    expect(within(card("Today's bookings")).getByText('0')).toBeInTheDocument()
    expect(within(card("Today's bookings")).getByText('1 Oct 2026')).toBeInTheDocument()
    expect(within(card('Utilization')).getByText('0.0% (0.0 of 1,288.0 h)')).toBeInTheDocument()
    expect(within(card('Agent success rate')).getByText('80.0% (20 of 25)')).toBeInTheDocument()
    expect(within(card('Avg agent processing time')).getByText('2.4 s (20 runs)')).toBeInTheDocument()
  })

  it('links to the approval queue, the agent runs and the reports', async () => {
    reportsHandlers()
    renderApp('/', { role: Roles.FacilitiesOfficer })

    const pending = await screen.findByRole('region', { name: 'Pending approvals' })
    expect(within(pending).getByRole('link', { name: 'Open the approval queue' })).toHaveAttribute('href', '/approvals')
    expect(within(card('Agent success rate')).getByRole('link', { name: 'Agent runs' })).toHaveAttribute('href', '/agent-runs')
    expect(within(card('Utilization')).getByRole('link', { name: 'Reports' })).toHaveAttribute('href', '/reports')
  })

  it('shows "—" with 0 denominators, never 0%, and empty charts', async () => {
    reportsHandlers({ dashboard: EMPTY_DASHBOARD })
    renderApp('/', { role: Roles.FacilitiesOfficer })

    expect(await within(await screen.findByRole('region', { name: 'Agent success rate' })).findByText('— (0 of 0)')).toBeInTheDocument()
    expect(within(card('Avg agent processing time')).getByText('— (0 runs)')).toBeInTheDocument()
    expect(within(card('Utilization')).getByText('— (0 h available)')).toBeInTheDocument()
    expect(within(card('Bookings per day')).getByText('No bookings in the last 7 days.')).toBeInTheDocument()
    expect(within(card('Utilization by building')).getByText('No active rooms.')).toBeInTheDocument()
  })

  it('shows the building figures as a table', async () => {
    reportsHandlers()
    const { user } = renderApp('/', { role: Roles.FacilitiesOfficer })

    const panel = await screen.findByRole('region', { name: 'Utilization by building' })
    await user.click(within(panel).getByRole('button', { name: 'Show as table' }))
    const table = within(panel).getByRole('table', { name: 'Utilization by building' })
    const row = within(table).getByRole('row', { name: /MB · Main Building/ })
    expect(row).toHaveTextContent('744.0 h')
    expect(row).toHaveTextContent('0.0%')
  })

  it('shows an error with Retry, which loads the dashboard again', async () => {
    const requests = reportsHandlers({ status: { dashboard: 500 } })
    const { user } = renderApp('/', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Could not load the dashboard: Something went wrong')).toBeInTheDocument()
    reportsHandlers()
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await within(await screen.findByRole('region', { name: 'Pending approvals' })).findByText('4')).toBeInTheDocument()
    expect(requests.dashboard).toBe(1)
  })

  it('gives an Admin links to their pages and calls no API', async () => {
    const urls: string[] = []
    server.events.on('request:start', ({ request }) => urls.push(request.url))
    renderApp('/', { role: Roles.Admin })

    expect(await screen.findByRole('heading', { level: 1, name: 'Dashboard' })).toBeInTheDocument()
    const main = screen.getByRole('main')
    expect(within(main).getByRole('link', { name: /Users/ })).toHaveAttribute('href', '/users')
    expect(within(main).getByRole('link', { name: /Clubs/ })).toHaveAttribute('href', '/clubs')
    expect(within(main).getByRole('link', { name: /Audit log/ })).toHaveAttribute('href', '/audit-logs')
    expect(screen.queryByRole('region', { name: 'Pending approvals' })).not.toBeInTheDocument()
    expect(urls).toEqual([])
    server.events.removeAllListeners()
  })
})
