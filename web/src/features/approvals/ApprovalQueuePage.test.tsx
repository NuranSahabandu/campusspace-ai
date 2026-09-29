import { screen, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { pageOf } from '../../test/fixtures'
import { renderApp } from '../../test/utils'
import { PENDING_DETAIL, QUEUE_PAGE } from './approvalsFixtures'
import { approvalsHandlers } from './approvalsHandlers'

// The captured queue also holds other pending requests; the demo request is the one PENDING_DETAIL shows.
const demo = QUEUE_PAGE.items.find((i) => i.requestId === PENDING_DETAIL.id)!

describe('ApprovalQueuePage', () => {
  it('lists the pending requests with room, quote and revision', async () => {
    approvalsHandlers()
    renderApp('/approvals', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { level: 1, name: 'Approvals' })).toBeInTheDocument()
    const row = (await screen.findByText(demo.purpose)).closest('[role="row"]') as HTMLElement
    expect(within(row).getByText(demo.requesterName)).toBeInTheDocument()
    expect(within(row).getByText(/Student · Robotics Club/)).toBeInTheDocument()
    expect(within(row).getByText('A301')).toBeInTheDocument()
    expect(within(row).getByText('LKR 5,500.00')).toBeInTheDocument()
  })

  it('shows "Fee-exempt" for an exempt quote', async () => {
    approvalsHandlers({ queue: pageOf([{ ...demo, exempt: true, draftTotal: 0 }]) })
    renderApp('/approvals', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Fee-exempt')).toBeInTheDocument()
  })

  it('shows the empty state', async () => {
    approvalsHandlers({ queue: pageOf([]) })
    renderApp('/approvals', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('No pending approvals')).toBeInTheDocument()
  })

  it('opens the approval detail on a row click', async () => {
    approvalsHandlers()
    const { user } = renderApp('/approvals', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByText(demo.purpose))

    expect(await screen.findByRole('link', { name: 'Back to approvals' })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 1, name: PENDING_DETAIL.purpose })).toBeInTheDocument()
  })

  it.each([Roles.Admin])('sends %s to /forbidden (the approval API is Officer-only)', async (role) => {
    renderApp('/approvals', { role })

    expect(await screen.findByText('You do not have permission to view this page.')).toBeInTheDocument()
  })
})
