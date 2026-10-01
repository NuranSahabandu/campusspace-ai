import { screen, waitFor, within } from '@testing-library/react'
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios'
import { http, HttpResponse } from 'msw'
import { Roles } from '../auth/roles'
import { requestsHandlers } from '../features/requests/requestsHandlers'
import { createQueryClient } from '../queryClient'
import { API, server } from '../test/server'
import { renderApp } from '../test/utils'
import { browser } from './client'
import { isPermanentClientError, MAIN_QUERY_META } from './forbidden'

const forbidden = () => HttpResponse.json({ title: 'Forbidden', status: 403 }, { status: 403 })

function httpError(status: number) {
  const response = { status, data: {}, headers: {}, config: { headers: new AxiosHeaders() } } as AxiosResponse
  return new AxiosError('failed', String(status), undefined, undefined, response)
}

describe('403 handling (plan §12)', () => {
  it('a main query (MAIN_QUERY_META) that gets a 403 opens the access-denied page', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {})
    const client = createQueryClient()

    await expect(
      client.fetchQuery({ queryKey: ['main'], queryFn: () => Promise.reject(httpError(403)), meta: MAIN_QUERY_META }),
    ).rejects.toThrow()

    expect(assign).toHaveBeenCalledWith('/forbidden')
  })

  it('a query without the meta never navigates on a 403', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {})
    const client = createQueryClient()

    await expect(client.fetchQuery({ queryKey: ['widget'], queryFn: () => Promise.reject(httpError(403)) })).rejects.toThrow()

    expect(assign).not.toHaveBeenCalled()
  })

  it('a secondary widget (the emails card) shows the 403 in place, without Retry or navigation', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {})
    requestsHandlers()
    server.use(http.get(`${API}/api/booking-requests/:id/notifications`, forbidden))
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    const emails = await screen.findByRole('region', { name: 'Emails to the requester' })
    expect(await within(emails).findByText("You don't have access to this.")).toBeInTheDocument()
    expect(within(emails).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
    // The rest of the page still works.
    expect(screen.getByRole('heading', { level: 1, name: 'Robotics Club Arduino workshop' })).toBeInTheDocument()
    expect(assign).not.toHaveBeenCalled()
  })

  it('a booking request 403 stays "Request not found" (its existence is not revealed) and never navigates', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {})
    requestsHandlers()
    server.use(http.get(`${API}/api/booking-requests/3`, forbidden))
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText(/Request not found/)).toBeInTheDocument()
    await waitFor(() => expect(assign).not.toHaveBeenCalled())
  })

  it.each([
    [400, true],
    [403, true],
    [404, true],
    [408, false],
    [429, false],
    [500, false],
  ])('a %i is a permanent client error: %s (no retry)', (status, permanent) => {
    expect(isPermanentClientError(httpError(status))).toBe(permanent)
  })

  it('a network error (no response) is retried', () => {
    expect(isPermanentClientError(new AxiosError('Network Error', 'ERR_NETWORK'))).toBe(false)
  })
})
