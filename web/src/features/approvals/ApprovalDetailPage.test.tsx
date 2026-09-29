import { screen, waitFor, within } from '@testing-library/react'
import { HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { renderApp } from '../../test/utils'
import { useToastStore } from '../../ui/toastStore'
import {
  APPROVED_DETAIL,
  CONFLICT_NEW_PROPOSAL,
  CONFLICT_TIME_CLOSED,
  DRAFT_QUOTE,
  PENDING_DETAIL,
  PENDING_RUN_DETAIL,
  PENDING_RUNS,
  POLICY_CHANGED_RUN_DETAIL,
  REVISED_RUN_DETAIL,
} from './approvalsFixtures'
import { approvalsHandlers } from './approvalsHandlers'

const route = `/approvals/${PENDING_DETAIL.id}`
const card = (name: string | RegExp) => screen.getByRole('region', { name })
const officer = { role: Roles.FacilitiesOfficer }

async function openDecision(user: ReturnType<typeof renderApp>['user'], button: string, dialogTitle: string) {
  await user.click(await screen.findByRole('button', { name: button }))
  return screen.findByRole('dialog', { name: dialogTitle })
}

describe('ApprovalDetailPage', () => {
  it('shows the request, the proposal with alternatives and source chips, and the .NET quotation', async () => {
    approvalsHandlers()
    renderApp(route, officer)

    expect(await screen.findByRole('heading', { level: 1, name: PENDING_DETAIL.purpose })).toBeInTheDocument()
    expect(within(card('Requester')).getByText('Student')).toBeInTheDocument()

    const proposal = await screen.findByRole('region', { name: 'Proposal (revision 1)' })
    expect(await within(proposal).findByTestId('chosen-room')).toHaveTextContent(
      'A301 · Computer Lab A301 (48 seats · Main Building)',
    )
    expect(within(proposal).getByText('48 seats for 45 attendees; has computers, projector; Main Building')).toBeInTheDocument()
    const alternatives = within(proposal).getByRole('list', { name: 'Alternative rooms' })
    expect(within(alternatives).getByText(/N201 · Computer Lab N201/)).toBeInTheDocument()
    const equipment = within(proposal).getByRole('table', { name: 'Proposed equipment' })
    expect(within(equipment).getByText('Portable')).toBeInTheDocument()
    expect(within(equipment).getByText('Built into the room')).toBeInTheDocument()

    const quote = within(card('Quotation'))
    expect(await quote.findByText('Computer lab A301, 3 h @ LKR 1,500')).toBeInTheDocument()
    expect(quote.getByTestId('quote-total')).toHaveTextContent('LKR 5,500.00')
  })

  it('takes every price from the quotation, never from the agent', async () => {
    approvalsHandlers({ quote: { ...DRAFT_QUOTE, subtotal: 1234, total: 1234 } })
    renderApp(route, officer)

    expect(await within(await screen.findByRole('region', { name: 'Quotation' })).findByTestId('quote-total')).toHaveTextContent(
      'LKR 1,234.00',
    )
  })

  it('shows the latest checklist green and red, with earlier attempts collapsed', async () => {
    const [latest, ...rest] = REVISED_RUN_DETAIL.validation
    // One failed rule, from the real response with V05 flipped.
    const failed = {
      ...latest,
      rules: latest.rules.map((r) => (r.rule === 'V05' ? { ...r, passed: false, message: 'Closes at 20:00 on Thursdays' } : r)),
    }
    approvalsHandlers({ run: { ...REVISED_RUN_DETAIL, validation: [failed, ...rest] } })
    renderApp(route, officer)

    const checklist = await screen.findByRole('list', { name: 'Validation checklist' })
    const rows = within(checklist).getAllByRole('listitem')
    expect(rows).toHaveLength(12)
    expect(rows.filter((r) => r.dataset.passed === 'true')).toHaveLength(11)
    const red = rows.find((r) => r.dataset.passed === 'false')!
    expect(red).toHaveTextContent('V05')
    expect(red).toHaveTextContent('Closes at 20:00 on Thursdays')
    expect(within(red).getByTitle('Failed')).toBeInTheDocument()
    expect(screen.getByText(`Earlier attempts (${rest.length})`)).toBeInTheDocument()
    expect(screen.getByText(`Attempt ${latest.attempt}: 1 of 12 rules failed`)).toBeInTheDocument()
  })

  it('shows the agent timeline with steps, tool calls as JSON text, and the officer summary', async () => {
    approvalsHandlers()
    const { user } = renderApp(route, officer)

    const timeline = await screen.findByRole('region', { name: 'Agent timeline' })
    const trajectory = await within(timeline).findByRole('list', { name: 'Agent trajectory' })
    expect(within(trajectory).getAllByRole('listitem').map((li) => li.textContent?.replace('→', ''))).toEqual(
      PENDING_RUN_DETAIL.nodes,
    )
    expect(within(timeline).getByTestId('officer-summary')).toHaveTextContent(PENDING_RUN_DETAIL.officerSummary!)

    await user.click(within(timeline).getByRole('button', { name: /^Step 1: supervisor/ }))
    // Later steps may call the same tool; the first is step 1's.
    const [args] = await within(timeline).findAllByLabelText('get_request_context arguments')
    expect(args.textContent).toBe(JSON.stringify(PENDING_RUN_DETAIL.steps[0].toolCalls[1].args, null, 2))
    expect(args.children).toHaveLength(0)
  })

  it('warns when the policy changed since the proposal, in plain words', async () => {
    approvalsHandlers({ run: POLICY_CHANGED_RUN_DETAIL })
    renderApp(route, officer)

    const banner = await screen.findByRole('alert', { name: 'Policy changed' })
    expect(banner).toHaveTextContent('The booking policy changed since this proposal')
    expect(within(banner).getByText('Opening hours')).toBeInTheDocument()
  })

  it('does not warn when the policy is unchanged', async () => {
    approvalsHandlers()
    renderApp(route, officer)

    await screen.findByTestId('chosen-room')
    expect(screen.queryByRole('alert', { name: 'Policy changed' })).not.toBeInTheDocument()
  })

  it('shows HTML in the requester notes as literal text', async () => {
    const payload = '<img src=x onerror="alert(1)"><script>alert(2)</script>'
    approvalsHandlers({ detail: { ...PENDING_DETAIL, notes: payload } })
    renderApp(route, officer)

    const notes = await screen.findByTestId('request-notes')
    expect(notes.textContent).toBe(payload)
    expect(notes.children).toHaveLength(0)
    expect(document.querySelector('img[src="x"]')).toBeNull()
  })

  it('approves: 200 shows Approved', async () => {
    const state = approvalsHandlers({
      decide: (_action, _body, s) => {
        s.detail = APPROVED_DETAIL
        return HttpResponse.json(APPROVED_DETAIL)
      },
    })
    const { user } = renderApp(route, officer)

    const dialog = await openDecision(user, 'Approve', 'Approve this proposal?')
    await user.type(within(dialog).getByLabelText('Comment (optional)'), 'Looks good')
    await user.click(within(dialog).getByRole('button', { name: 'Approve' }))

    await waitFor(() => expect(useToastStore.getState().current?.message).toBe('Request approved'))
    expect(await screen.findByText('Approved: the booking is confirmed and the quotation issued.')).toBeInTheDocument()
    expect(state.decisions).toEqual([{ action: 'approve', body: { comment: 'Looks good' } }])
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('approves: 202 shows "Approval in progress…" and then Approved after a refetch', async () => {
    const state = approvalsHandlers({
      decide: () => HttpResponse.json({ requestId: PENDING_DETAIL.id, status: 'ApprovalInProgress' }, { status: 202 }),
    })
    const { user } = renderApp(route, officer)

    const dialog = await openDecision(user, 'Approve', 'Approve this proposal?')
    await user.click(within(dialog).getByRole('button', { name: 'Approve' }))

    expect(await screen.findByTestId('approval-in-progress')).toHaveTextContent('Approval in progress…')
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(state.decisions[0].body).toEqual({ comment: null })

    // The poller finishes the approval; the page's 3 s refetch picks it up.
    state.detail = APPROVED_DETAIL
    expect(
      await screen.findByText('Approved: the booking is confirmed and the quotation issued.', undefined, { timeout: 8000 }),
    ).toBeInTheDocument()
    expect(screen.queryByTestId('approval-in-progress')).not.toBeInTheDocument()
  }, 15_000)

  it.each([
    ['a new proposal', CONFLICT_NEW_PROPOSAL],
    ['the time is no longer valid', CONFLICT_TIME_CLOSED],
  ])('approve 409 (%s) shows the server message exactly as sent and refetches', async (_name, problem) => {
    const state = approvalsHandlers({ decide: () => HttpResponse.json(problem, { status: 409 }) })
    const { user } = renderApp(route, officer)
    await screen.findByTestId('chosen-room')
    const fetchesBefore = state.detailFetches

    const dialog = await openDecision(user, 'Approve', 'Approve this proposal?')
    await user.click(within(dialog).getByRole('button', { name: 'Approve' }))

    expect(await screen.findByTestId('decision-conflict')).toHaveTextContent(problem.title)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(useToastStore.getState().current).toBeNull()
    await waitFor(() => expect(state.detailFetches).toBeGreaterThan(fetchesBefore))
  })

  it('reject needs a reason and sends it', async () => {
    const state = approvalsHandlers({
      decide: (_a, body, s) => {
        s.detail = { ...PENDING_DETAIL, status: 'Rejected', updatedAt: '2026-09-29T07:00:00Z' }
        return HttpResponse.json({ ...s.detail, reason: body.reason })
      },
    })
    const { user } = renderApp(route, officer)

    const dialog = await openDecision(user, 'Reject', 'Reject this request?')
    await user.type(within(dialog).getByLabelText(/Reason/), '   ')
    await user.click(within(dialog).getByRole('button', { name: 'Reject' }))
    expect(await within(dialog).findByText('A reason is required')).toBeInTheDocument()
    expect(state.decisions).toHaveLength(0)

    await user.type(within(dialog).getByLabelText(/Reason/), 'Exam week')
    await user.click(within(dialog).getByRole('button', { name: 'Reject' }))
    await waitFor(() => expect(useToastStore.getState().current?.message).toBe('Request rejected'))
    expect(state.decisions).toEqual([{ action: 'reject', body: { reason: 'Exam week' } }])
  })

  it('request revision needs notes, then shows "New proposal being prepared"', async () => {
    const state = approvalsHandlers({
      decide: (_a, _b, s) => {
        s.detail = { ...PENDING_DETAIL, status: 'AgentProcessing', updatedAt: '2026-09-29T07:00:00Z' }
        return HttpResponse.json(s.detail, { status: 202 })
      },
    })
    const { user } = renderApp(route, officer)

    const dialog = await openDecision(user, 'Request revision', 'Request a revision?')
    await user.click(within(dialog).getByRole('button', { name: 'Request revision' }))
    expect(await within(dialog).findByText('Notes are required')).toBeInTheDocument()
    expect(state.decisions).toHaveLength(0)

    await user.type(within(dialog).getByLabelText(/Notes for the agents/), 'Use a lab in the New Building')
    await user.click(within(dialog).getByRole('button', { name: 'Request revision' }))

    expect(await screen.findByTestId('preparing')).toHaveTextContent('New proposal being prepared')
    expect(state.decisions).toEqual([{ action: 'request-revision', body: { notes: 'Use a lab in the New Building' } }])
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('limits the text to 1000 characters', async () => {
    approvalsHandlers()
    const { user } = renderApp(route, officer)

    const dialog = await openDecision(user, 'Reject', 'Reject this request?')
    expect(within(dialog).getByLabelText(/Reason/)).toHaveAttribute('maxlength', '1000')
  })

  it.each([
    ['Approved', APPROVED_DETAIL],
    ['AgentProcessing', { ...PENDING_DETAIL, status: 'AgentProcessing' }],
  ])('hides the decision buttons when the request is %s', async (_status, detail) => {
    approvalsHandlers({ detail })
    renderApp(route, officer)

    expect(await screen.findByRole('heading', { level: 1 })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Request revision' })).not.toBeInTheDocument()
  })

  it('hides the decision buttons while the live run is not awaiting approval', async () => {
    approvalsHandlers({ runs: [{ ...PENDING_RUNS[0], status: 'Resuming' }] })
    renderApp(route, officer)

    await screen.findByTestId('chosen-room')
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('shows "Request not found" for an unknown request', async () => {
    approvalsHandlers()
    renderApp('/approvals/999999', officer)

    expect(await screen.findByText(/Request not found/)).toBeInTheDocument()
  })
})
