import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { AgentRunMetricsDto } from '../../api/types'
import { Roles } from '../../auth/roles'
import { pageOf } from '../../test/fixtures'
import { renderApp } from '../../test/utils'
import { campusAddDays, campusToday } from '../../ui/formatDateTime'
import { PENDING_DETAIL } from '../approvals/approvalsFixtures'
import { approvalsHandlers } from '../approvals/approvalsHandlers'
import { METRICS, RUNS_PAGE } from './agentRunsFixtures'
import { agentRunsHandlers } from './agentRunsHandlers'

// Hand-written (not captured): what the API answers for a range with no runs (every denominator 0, rates null).
const EMPTY_METRICS: AgentRunMetricsDto = {
  from: '2030-01-01',
  to: '2030-01-07',
  runs: {
    total: 0,
    byStatus: { Queued: 0, Running: 0, AwaitingApproval: 0, Resuming: 0, Completed: 0, Rejected: 0, Failed: 0, Cancelled: 0 },
    inProgress: 0,
    finished: 0,
    reachedGate: 0,
    failedBeforeGate: 0,
    successRate: null,
    reachedGateProcessing: { runs: 0, withoutSteps: 0, avgMs: null, p95Ms: null },
    failedBeforeGateProcessing: { runs: 0, withoutSteps: 0, avgMs: null, p95Ms: null },
    runsWithUsage: 0,
    totalTokens: 0,
    avgTokensPerRun: null,
    llmAttemptedRuns: 0,
    fallbackRuns: 0,
    fallbackRate: null,
  },
  agents: [],
}

const card = (title: string) => screen.getByRole('region', { name: title })
const failedRow = RUNS_PAGE.items.find((r) => r.status === 'Failed' && r.failureReason)!

describe('AgentRunsPage', () => {
  it('shows the run metrics, each with its denominator', async () => {
    agentRunsHandlers()
    renderApp('/agent-runs', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { level: 1, name: 'Agent runs' })).toBeInTheDocument()
    await screen.findByRole('region', { name: 'Success rate' })
    // The captured range: 15 of 20 runs reached the gate, 3 LLM runs and none fell back, 17,811 tokens over 3 runs.
    expect(await within(card('Success rate')).findByText('75.0% (15 of 20)')).toBeInTheDocument()
    expect(within(card('Fallback rate')).getByText('0.0% (0 of 3)')).toBeInTheDocument()
    expect(within(card('Avg tokens per run')).getByText('5,937')).toBeInTheDocument()
    expect(within(card('Avg tokens per run')).getByText('17,811 tokens over 3 runs with usage')).toBeInTheDocument()
    expect(within(card('Agent processing time')).getByText('avg 3.1 s · p95 16.4 s (15 runs)')).toBeInTheDocument()
    expect(
      within(card('Agent processing time')).getByText('Failed runs: avg 36 ms · p95 37 ms (2 runs) · 3 without steps'),
    ).toBeInTheDocument()
  })

  it('shows the per-agent table and latency bars', async () => {
    agentRunsHandlers()
    renderApp('/agent-runs', { role: Roles.FacilitiesOfficer })

    const table = await screen.findByRole('table', { name: 'Per-agent metrics' })
    const supervisor = METRICS.agents.find((a) => a.agent === 'supervisor')!
    const row = within(table).getByRole('rowheader', { name: 'supervisor' }).closest('tr')!
    expect(within(row).getByText(`25.0% (${supervisor.llmSteps} of ${supervisor.steps})`)).toBeInTheDocument()
    // policy_cost made no LLM attempt in this range: its fallback rate has no denominator.
    const policy = within(table).getByRole('rowheader', { name: 'policy_cost' }).closest('tr')!
    expect(within(policy).getByText('— (0 of 0)')).toBeInTheDocument()

    expect(screen.getByRole('img', { name: 'supervisor average 1.7 s' })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'supervisor p95 7.2 s' })).toBeInTheDocument()
  })

  it('asks for the last 7 campus days by default, for the metrics and the list', async () => {
    const requests = agentRunsHandlers()
    renderApp('/agent-runs', { role: Roles.FacilitiesOfficer })

    const today = campusToday()
    await waitFor(() => expect(requests.metrics.at(-1)?.get('to')).toBe(today))
    expect(requests.metrics.at(-1)?.get('from')).toBe(campusAddDays(today, -6))
    await waitFor(() => expect(requests.list.at(-1)?.get('from')).toBe(campusAddDays(today, -6)))
    expect(requests.list.at(-1)?.get('sort')).toBe('-createdAt')
  })

  it('sends the filters', async () => {
    const requests = agentRunsHandlers()
    const { user } = renderApp('/agent-runs', { role: Roles.FacilitiesOfficer })
    await screen.findByText(RUNS_PAGE.items[0].purpose, {}, { timeout: 3000 })

    await user.click(screen.getByRole('combobox', { name: 'Status' }))
    await user.click(await screen.findByRole('option', { name: 'Failed' }))
    await waitFor(() => expect(requests.list.at(-1)?.getAll('status')).toEqual(['Failed']))
    await user.keyboard('{Escape}')

    await user.click(screen.getByRole('button', { name: 'Fell back' }))
    await waitFor(() => expect(requests.list.at(-1)?.get('fallback')).toBe('true'))
    await user.click(screen.getByRole('button', { name: 'No fallback' }))
    await waitFor(() => expect(requests.list.at(-1)?.get('fallback')).toBe('false'))

    await user.type(screen.getByLabelText('Request number'), '12x')
    await waitFor(() => expect(requests.list.at(-1)?.get('requestId')).toBe('12'))

    await user.type(screen.getByLabelText('Request purpose'), 'Robotics')
    await waitFor(() => expect(requests.list.at(-1)?.get('search')).toBe('Robotics'))

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-09-30' } })
    await waitFor(() => expect(requests.metrics.at(-1)?.get('to')).toBe('2026-09-30'))
    expect(requests.metrics.at(-1)?.get('from')).toBe('2026-09-01')

    const last = requests.list.at(-1)!
    expect(last.get('to')).toBe('2026-09-30')
    expect(last.getAll('status')).toEqual(['Failed'])
    expect(last.get('fallback')).toBe('false')
    expect(last.get('requestId')).toBe('12')
  })

  it('shows "— (0 of 0)", never 0%, and the empty states', async () => {
    agentRunsHandlers({ metrics: EMPTY_METRICS, page: pageOf([]) })
    renderApp('/agent-runs?from=2030-01-01&to=2030-01-07', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('No agent runs in this range')).toBeInTheDocument()
    expect(within(card('Success rate')).getByText('— (0 of 0)')).toBeInTheDocument()
    expect(within(card('Fallback rate')).getByText('— (0 of 0)')).toBeInTheDocument()
    expect(within(card('Agent processing time')).getByText('— (0 runs)')).toBeInTheDocument()
    expect(screen.queryByText(/0\.0%/)).toBeNull()
    expect(screen.queryByRole('table', { name: 'Per-agent metrics' })).toBeNull()
    expect(await screen.findByText('No agent runs match these filters')).toBeInTheDocument()
  })

  it('shows a metrics error with Retry', async () => {
    agentRunsHandlers({ metricsStatus: 500 })
    renderApp('/agent-runs', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Could not load the agent metrics: Metrics are unavailable')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('shows the failure reason as plain text', async () => {
    const hostile = { ...failedRow, failureReason: '<b>bold</b> & <script>x</script>' }
    agentRunsHandlers({ page: pageOf([hostile]) })
    renderApp('/agent-runs', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('<b>bold</b> & <script>x</script>')).toBeInTheDocument()
  })

  it('opens a finished run in the run detail', async () => {
    agentRunsHandlers({ page: pageOf([failedRow]) })
    const { user } = renderApp('/agent-runs', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByText(failedRow.purpose))

    expect(await screen.findByRole('link', { name: 'Back to agent runs' })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 1, name: /^Agent run · revision/ })).toBeInTheDocument()
  })

  it('opens a run waiting for a decision in the approval page', async () => {
    approvalsHandlers()
    const awaiting = { ...failedRow, status: 'AwaitingApproval', requestId: PENDING_DETAIL.id, failureReason: null }
    agentRunsHandlers({ page: pageOf([awaiting]) })
    const { user } = renderApp('/agent-runs', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByText(awaiting.purpose))

    expect(await screen.findByRole('link', { name: 'Back to approvals' })).toBeInTheDocument()
  })

  it('sends Admin to /forbidden (the agent-run API is Officer-only)', async () => {
    renderApp('/agent-runs', { role: Roles.Admin })

    expect(await screen.findByText('You do not have permission to view this page.')).toBeInTheDocument()
  })
})
