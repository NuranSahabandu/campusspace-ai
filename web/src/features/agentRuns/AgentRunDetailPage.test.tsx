import { screen, within } from '@testing-library/react'
import { Roles } from '../../auth/roles'
import { renderApp } from '../../test/utils'
import { FAILED_RUN_DETAIL } from './agentRunsFixtures'
import { agentRunsHandlers } from './agentRunsHandlers'

const RUN = '/agent-runs/8153a27b-08be-40a8-9f6d-525fb794dac3'

describe('AgentRunDetailPage', () => {
  it('shows the run, its trace, decisions and the policy snapshot the agents used', async () => {
    agentRunsHandlers()
    renderApp(RUN, { role: Roles.FacilitiesOfficer })

    expect(
      await screen.findByRole('heading', { level: 1, name: `Agent run · revision ${FAILED_RUN_DETAIL.revisionNo}` }),
    ).toBeInTheDocument()
    expect(screen.getByRole('alert', { name: 'Failure reason' })).toHaveTextContent(FAILED_RUN_DETAIL.failureReason!)
    expect(screen.getByRole('link', { name: `Open request #${FAILED_RUN_DETAIL.requestId}` })).toHaveAttribute(
      'href',
      `/requests/${FAILED_RUN_DETAIL.requestId}`,
    )
    expect(screen.queryByRole('link', { name: 'Review proposal' })).toBeNull()

    const snapshot = within(screen.getByRole('region', { name: 'Policy snapshot' })).getByLabelText('Policy snapshot')
    expect(snapshot).toHaveTextContent('max_duration_hours')
    expect(screen.getByRole('button', { name: /^Step 1: supervisor/ })).toBeInTheDocument()
    const decision = FAILED_RUN_DETAIL.decisions[0]
    expect(within(screen.getByRole('region', { name: 'Decisions' })).getByText(new RegExp(`^${decision.decision} · `))).toBeInTheDocument()
  })

  it('shows officer text and the failure reason as plain text', async () => {
    agentRunsHandlers({
      detail: { ...FAILED_RUN_DETAIL, failureReason: '<img src=x onerror=alert(1)>', officerSummary: '<b>not bold</b>' },
    })
    renderApp(RUN, { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('alert', { name: 'Failure reason' })).toHaveTextContent('<img src=x onerror=alert(1)>')
    expect(within(screen.getByRole('region', { name: 'Officer summary' })).getByText('<b>not bold</b>')).toBeInTheDocument()
    expect(document.querySelector('img[src="x"]')).toBeNull()
  })

  it('warns about a changed policy only while the run waits for a decision', async () => {
    const changed = { ...FAILED_RUN_DETAIL, policyChangedKeys: ['max_duration_hours'] }
    agentRunsHandlers({ detail: changed })
    const { unmount } = renderApp(RUN, { role: Roles.FacilitiesOfficer })
    await screen.findByRole('heading', { level: 1 })
    expect(screen.queryByRole('alert', { name: 'Policy changed' })).toBeNull()
    unmount()

    agentRunsHandlers({ detail: { ...changed, status: 'AwaitingApproval', failureReason: null } })
    renderApp(RUN, { role: Roles.FacilitiesOfficer })
    expect(await screen.findByRole('alert', { name: 'Policy changed' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Review proposal' })).toHaveAttribute(
      'href',
      `/approvals/${FAILED_RUN_DETAIL.requestId}`,
    )
  })

  it('shows "not found" for an unknown or malformed id', async () => {
    agentRunsHandlers()
    const { unmount } = renderApp('/agent-runs/00000000-0000-0000-0000-000000000404', { role: Roles.FacilitiesOfficer })
    expect(await screen.findByText('Agent run not found.')).toBeInTheDocument()
    unmount()

    renderApp('/agent-runs/not-a-guid', { role: Roles.FacilitiesOfficer })
    expect(await screen.findByText('Agent run not found.')).toBeInTheDocument()
  })

  it('sends Admin to /forbidden', async () => {
    renderApp(RUN, { role: Roles.Admin })

    expect(await screen.findByText('You do not have permission to view this page.')).toBeInTheDocument()
  })
})
