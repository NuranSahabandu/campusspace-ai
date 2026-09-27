import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { PRICING_NOW } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { useToastStore } from '../../ui/toastStore'
import { pricingHandlers } from './pricingHandlers'
import { IN_EFFECT_TOOLTIP, PRICING_EXPLAINER } from './pricingValues'

const matrixRow = (label: string) => within(screen.getByRole('table', { name: 'Current prices' })).getByRole('row', { name: new RegExp(`^${label}`) })

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(PRICING_NOW)
})
afterEach(() => {
  vi.useRealTimers()
})

describe('PricingPage', () => {
  it('shows the current prices matrix with rates, exemptions, start dates and gaps', async () => {
    pricingHandlers()
    renderApp('/pricing', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { name: 'Pricing' })).toBeInTheDocument()
    expect(screen.getByText(PRICING_EXPLAINER)).toBeInTheDocument()
    await screen.findByRole('table', { name: 'Current prices' })

    const lab = matrixRow('Computer lab')
    expect(within(lab).getByText('LKR 1,500.00 / h')).toBeInTheDocument()
    expect(within(lab).getByText('Exempt')).toBeInTheDocument()
    expect(within(lab).getAllByText('from 1 Jan 2026')).toHaveLength(2)
    expect(within(matrixRow('Auditorium')).getByText('No rule')).toBeInTheDocument()
  })

  it('lists rules with rate, start date and status, sorted by room type', async () => {
    const { listRequests } = pricingHandlers()
    renderApp('/pricing', { role: Roles.FacilitiesOfficer })

    const scheduled = (await screen.findByRole('button', { name: 'Edit SeminarRoom Student from 1 Nov 2026' })).closest(
      '[role="row"]',
    ) as HTMLElement
    expect(within(scheduled).getByText('LKR 750.00 / h')).toBeInTheDocument()
    expect(within(scheduled).getByText('1 Nov 2026')).toBeInTheDocument()
    expect(within(scheduled).getByText('Scheduled')).toBeInTheDocument()
    expect(listRequests[0].get('sort')).toBe('roomType')
    expect(listRequests[0].has('includeHistory')).toBe(false)
  })

  it('sends the room type, role and history filters', async () => {
    const { listRequests } = pricingHandlers()
    const { user } = renderApp('/pricing', { role: Roles.FacilitiesOfficer })
    await screen.findByRole('button', { name: 'Edit SeminarRoom Student from 1 Nov 2026' })

    await user.click(screen.getByRole('combobox', { name: 'Room type' }))
    await user.click(await screen.findByRole('option', { name: 'Seminar room' }))
    await user.click(screen.getByRole('combobox', { name: 'Requester role' }))
    await user.click(await screen.findByRole('option', { name: 'Student' }))
    await user.click(screen.getByRole('switch', { name: 'Show superseded' }))

    await waitFor(() => expect(listRequests.at(-1)?.get('includeHistory')).toBe('true'))
    expect(listRequests.at(-1)?.get('roomType')).toBe('SeminarRoom')
    expect(listRequests.at(-1)?.get('requesterRole')).toBe('Student')
    expect(listRequests.at(-1)?.get('page')).toBe('1')
  })

  it('sorts by rate on the server', async () => {
    const { listRequests } = pricingHandlers()
    const { user } = renderApp('/pricing', { role: Roles.FacilitiesOfficer })
    await screen.findByRole('button', { name: 'Edit SeminarRoom Student from 1 Nov 2026' })

    await user.click(screen.getByRole('columnheader', { name: 'Rate' }))

    await waitFor(() => expect(listRequests.at(-1)?.get('sort')).toBe('hourlyRate'))
  })

  it('offers Edit and Delete only for scheduled rules and explains why otherwise', async () => {
    pricingHandlers()
    const { user } = renderApp('/pricing', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('button', { name: 'Edit SeminarRoom Student from 1 Nov 2026' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Delete SeminarRoom Student from 1 Nov 2026' })).toBeEnabled()
    const current = screen.getByRole('button', { name: 'Edit ComputerLab Student from 1 Jan 2026' })
    expect(current).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Delete ComputerLab Student from 1 Jan 2026' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Edit SeminarRoom Student from 1 Jun 2025' })).toBeDisabled()

    await user.hover(current.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent(IN_EFFECT_TOOLTIP)
  })

  it('deletes a scheduled rule after confirmation and refreshes both lists', async () => {
    const { listRequests, calls } = pricingHandlers()
    let deleted = false
    server.use(
      http.delete(`${API}/api/pricing-rules/9`, () => {
        deleted = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/pricing', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'Delete SeminarRoom Student from 1 Nov 2026' }))
    const dialog = await screen.findByRole('dialog', { name: 'Delete pricing rule?' })
    expect(within(dialog).getByText(/LKR 750.00 \/ h/)).toBeInTheDocument()
    const listsBefore = listRequests.length
    const currentBefore = calls.current
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(useToastStore.getState().current).toMatchObject({ message: 'Pricing rule deleted' }))
    expect(deleted).toBe(true)
    expect(listRequests.length).toBeGreaterThan(listsBefore)
    expect(calls.current).toBeGreaterThan(currentBefore)
  })

  it('shows an empty state when there are no rules', async () => {
    server.use(
      http.get(`${API}/api/pricing-rules/current`, () => HttpResponse.json([])),
      http.get(`${API}/api/pricing-rules`, () => HttpResponse.json({ items: [], page: 1, pageSize: 20, total: 0 })),
    )
    renderApp('/pricing', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('No pricing rules yet')).toBeInTheDocument()
  })

  it('shows errors with Retry when the current prices and the list fail', async () => {
    const calls = { current: 0, list: 0 }
    const failure = () => HttpResponse.json({ status: 500, title: 'An unexpected error occurred' }, { status: 500 })
    server.use(
      http.get(`${API}/api/pricing-rules/current`, () => {
        calls.current += 1
        return failure()
      }),
      http.get(`${API}/api/pricing-rules`, () => {
        calls.list += 1
        return failure()
      }),
    )
    const { user } = renderApp('/pricing', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText(/Could not load current prices/)).toBeInTheDocument()
    expect(await screen.findByText(/Could not load pricing rules/)).toBeInTheDocument()
    const [retryCurrent, retryList] = screen.getAllByRole('button', { name: 'Retry' })
    await user.click(retryCurrent)
    await user.click(retryList)
    await waitFor(() => expect(calls).toEqual({ current: 2, list: 2 }))
  })
})
