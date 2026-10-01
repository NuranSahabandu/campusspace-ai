import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import type { NotificationLogDto } from '../../api/types'
import { Roles } from '../../auth/roles'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { approvalsHandlers } from '../approvals/approvalsHandlers'
import { requestsHandlers } from '../requests/requestsHandlers'
import { FAILED_REJECTED, SKIPPED_APPROVED, SKIPPED_REJECTED } from './notificationsFixtures'

const officer = { role: Roles.FacilitiesOfficer }
const emails = () => screen.getByRole('region', { name: 'Emails to the requester' })

describe('EmailStatusCard', () => {
  it('shows a skipped approval email with its kind, recipient, attempts and reason', async () => {
    requestsHandlers({ notifications: SKIPPED_APPROVED })
    renderApp('/requests/3', officer)

    const card = await screen.findByRole('region', { name: 'Emails to the requester' })
    expect(await within(card).findByText('Booking confirmed (with calendar file)')).toBeInTheDocument()
    expect(within(card).getByText('Skipped')).toBeInTheDocument()
    expect(within(card).getByText('lecturer@campusspace.local')).toBeInTheDocument()
    expect(within(card).getByText('Email is not configured (Email:BrevoApiKey is empty)')).toBeInTheDocument()
    expect(within(card).getByRole('cell', { name: '1' })).toBeInTheDocument()
  })

  it('shows a failed, redirected email with its error on the approval screen', async () => {
    approvalsHandlers({ notifications: [...FAILED_REJECTED, ...SKIPPED_REJECTED] })
    renderApp('/approvals/42', officer)

    const card = await screen.findByRole('region', { name: 'Emails to the requester' })
    expect(await within(card).findByText('HTTP 401 (unauthorized)')).toBeInTheDocument()
    expect(within(card).getByText('Failed')).toBeInTheDocument()
    expect(within(card).getByText('(redirected)')).toBeInTheDocument()
    expect(within(card).getAllByText('Not approved')).toHaveLength(2)
  })

  it('renders an error as plain text, never as HTML', async () => {
    // Hand-written: the server's errors are fixed texts, this checks the page would not interpret one.
    const hostile: NotificationLogDto = { ...FAILED_REJECTED[0], error: '<b>bold</b> <img src=x onerror=alert(1)>' }
    requestsHandlers({ notifications: [hostile] })
    const { container } = renderApp('/requests/3', officer)

    expect(await within(await screen.findByRole('region', { name: 'Emails to the requester' }))
      .findByText('<b>bold</b> <img src=x onerror=alert(1)>')).toBeInTheDocument()
    expect(container.querySelector('b, img')).toBeNull()
  })

  it('says when there are no emails yet', async () => {
    requestsHandlers()
    renderApp('/requests/3', officer)

    expect(await within(await screen.findByRole('region', { name: 'Emails to the requester' })).findByText('No emails yet'))
      .toBeInTheDocument()
  })

  it('shows a sent email with its sent time', async () => {
    // Hand-written from the captured shape: a real Sent row needs a real delivery.
    const sent: NotificationLogDto = {
      ...SKIPPED_APPROVED[0],
      status: 'Sent',
      error: null,
      sentAt: '2026-10-01T02:20:42Z',
    }
    requestsHandlers({ notifications: [sent] })
    renderApp('/requests/3', officer)

    const card = await screen.findByRole('region', { name: 'Emails to the requester' })
    expect(await within(card).findByText('Sent')).toBeInTheDocument()
    expect(within(card).getByText(/1 Oct 2026, 07:50/)).toBeInTheDocument()
  })

  it('re-fetches while an email is pending, and shows a load error with a retry', async () => {
    let calls = 0
    requestsHandlers()
    server.use(
      http.get(`${API}/api/booking-requests/:id/notifications`, () => {
        calls++
        return calls === 1
          ? HttpResponse.json({ title: 'Boom', status: 500, traceId: 't' }, { status: 500 })
          : HttpResponse.json([{ ...SKIPPED_APPROVED[0], status: 'Pending', attempts: 0, error: null }])
      }),
    )
    const { user } = renderApp('/requests/3', officer)

    expect(await within(await screen.findByRole('region', { name: 'Emails to the requester' }))
      .findByText(/Could not load the emails: Boom/)).toBeInTheDocument()
    await user.click(within(emails()).getByRole('button', { name: 'Retry' }))
    expect(await within(emails()).findByText('Pending')).toBeInTheDocument()
    await waitFor(() => expect(calls).toBeGreaterThan(2), { timeout: 7_000 })
  }, 10_000)
})
