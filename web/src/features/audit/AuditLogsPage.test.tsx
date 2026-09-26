import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { AUDIT_LOGS, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'

describe('AuditLogsPage', () => {
  it('renders rows newest first with changed-property chips', async () => {
    let query: URLSearchParams | undefined
    server.use(
      http.get(`${API}/api/audit-logs`, ({ request }) => {
        query = new URL(request.url).searchParams
        return HttpResponse.json(pageOf(AUDIT_LOGS))
      }),
    )
    renderApp('/audit-logs', { role: Roles.Admin })

    const updated = (await screen.findByText('Updated')).closest('[role="row"]') as HTMLElement
    expect(within(updated).getByText('Role')).toBeInTheDocument()
    expect(within(updated).getByText('IsActive')).toBeInTheDocument()
    expect(within(updated).getByText(/14:15/)).toBeInTheDocument() // 08:45 UTC in Colombo

    const failed = screen.getByText('LoginFailed').closest('[role="row"]') as HTMLElement
    expect(within(failed).getByText('email: nobody@campusspace.local')).toBeInTheDocument()
    expect(within(failed).getAllByText('—')).toHaveLength(2) // no user, no entity id

    const deleted = screen.getByText('Deleted').closest('[role="row"]') as HTMLElement
    expect(within(deleted).getByText('—')).toBeInTheDocument() // {} details

    expect(query?.get('sort')).toBe('-at')
  })

  it('filters by action', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API}/api/audit-logs`, ({ request }) => {
        requests.push(new URL(request.url).searchParams)
        return HttpResponse.json(pageOf(AUDIT_LOGS))
      }),
    )
    const { user } = renderApp('/audit-logs', { role: Roles.Admin })

    await screen.findByText('Updated')
    expect(requests.at(-1)?.has('action')).toBe(false)

    await user.click(screen.getByRole('combobox', { name: 'Action' }))
    await user.click(await screen.findByRole('option', { name: 'LoginFailed' }))

    await waitFor(() => expect(requests.at(-1)?.get('action')).toBe('LoginFailed'))
  })

  it('sends date filters as whole campus-time days', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API}/api/audit-logs`, ({ request }) => {
        requests.push(new URL(request.url).searchParams)
        return HttpResponse.json(pageOf(AUDIT_LOGS))
      }),
    )
    const { user } = renderApp('/audit-logs', { role: Roles.Admin })
    await screen.findByText('Updated')

    await user.type(screen.getByLabelText('From'), '2026-09-26')
    await user.type(screen.getByLabelText('To'), '2026-09-26')

    await waitFor(() => {
      expect(requests.at(-1)?.get('from')).toBe('2026-09-26T00:00:00+05:30')
      expect(requests.at(-1)?.get('to')).toBe('2026-09-26T23:59:59.999+05:30')
    })
  })
})
