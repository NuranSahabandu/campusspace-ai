import { http, HttpResponse } from 'msw'
import type {
  AgentRunDetailDto,
  AgentRunSummaryDto,
  ApprovalQueueItemDto,
  BookingRequestDetailDto,
  PagedResult,
  QuotationDto,
} from '../../api/types'
import { API, server } from '../../test/server'
import { DRAFT_QUOTE, PENDING_DETAIL, PENDING_RUN_DETAIL, PENDING_RUNS, QUEUE_PAGE } from './approvalsFixtures'

export type Decision = 'approve' | 'reject' | 'request-revision'

interface Options {
  queue?: PagedResult<ApprovalQueueItemDto>
  detail?: BookingRequestDetailDto
  runs?: AgentRunSummaryDto[]
  run?: AgentRunDetailDto
  /** null: the request has no live quote (404). */
  quote?: QuotationDto | null
  /** The answer to a decision POST; it may change what the reads return next (state.detail = ...). */
  decide?: (action: Decision, body: Record<string, unknown>, state: State) => Response
}

export interface State {
  detail: BookingRequestDetailDto
  run: AgentRunDetailDto
  quote: QuotationDto | null
  /** Every decision POST, in order. */
  decisions: { action: Decision; body: Record<string, unknown> }[]
  /** How many times the request detail was fetched. */
  detailFetches: number
}

/**
 * The approval screens' endpoints over real captured responses (approvalsFixtures.ts). The reads follow `state`, so a
 * decision handler can move the request on (for example to Approved on the next fetch).
 */
export function approvalsHandlers(options: Options = {}) {
  const state: State = {
    detail: options.detail ?? PENDING_DETAIL,
    run: options.run ?? PENDING_RUN_DETAIL,
    quote: options.quote === undefined ? DRAFT_QUOTE : options.quote,
    decisions: [],
    detailFetches: 0,
  }
  const runs = options.runs ?? PENDING_RUNS
  server.use(
    http.get(`${API}/api/approvals/queue`, () => HttpResponse.json(options.queue ?? QUEUE_PAGE)),
    http.get(`${API}/api/booking-requests/:id`, ({ params }) => {
      if (String(state.detail.id) !== params.id)
        return HttpResponse.json({ title: 'Not Found', status: 404, traceId: 'trace-404' }, { status: 404 })
      state.detailFetches++
      return HttpResponse.json(state.detail)
    }),
    http.get(`${API}/api/booking-requests/:id/agent-runs`, () => HttpResponse.json(runs)),
    http.get(`${API}/api/agent-runs/:runId`, () => HttpResponse.json(state.run)),
    http.get(`${API}/api/booking-requests/:id/quotation`, () =>
      state.quote
        ? HttpResponse.json(state.quote)
        : HttpResponse.json({ title: 'Not Found', status: 404, traceId: 'trace-404' }, { status: 404 }),
    ),
    ...(['approve', 'reject', 'request-revision'] as const).map((action) =>
      http.post(`${API}/api/booking-requests/:id/${action}`, async ({ request }) => {
        const body = (await request.json()) as Record<string, unknown>
        state.decisions.push({ action, body })
        return options.decide
          ? options.decide(action, body, state)
          : HttpResponse.json({ title: 'Unexpected decision', status: 500 }, { status: 500 })
      }),
    ),
  )
  return state
}
