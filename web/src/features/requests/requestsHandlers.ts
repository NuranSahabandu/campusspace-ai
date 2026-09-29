import { http, HttpResponse } from 'msw'
import type { AgentRunSummaryDto, BookingRequestSummaryDto } from '../../api/types'
import { BOOKING_REQUEST_DETAILS, BOOKING_REQUESTS, REQUEST_CLUBS, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'

/**
 * The read endpoints both request pages use. Returns the recorded list queries (listRequests). The list ignores the
 * filters; the detail returns 404 Problem Details for an unknown id and 403 for id 403. Agent runs are `runs` for every
 * request (none by default).
 */
export function requestsHandlers({
  rows = BOOKING_REQUESTS,
  runs = [],
}: { rows?: BookingRequestSummaryDto[]; runs?: AgentRunSummaryDto[] } = {}) {
  const listRequests: URLSearchParams[] = []
  server.use(
    http.get(`${API}/api/booking-requests/:id/agent-runs`, () => HttpResponse.json(runs)),
    http.get(`${API}/api/clubs`, () => HttpResponse.json(pageOf(REQUEST_CLUBS))),
    http.get(`${API}/api/booking-requests`, ({ request }) => {
      listRequests.push(new URL(request.url).searchParams)
      return HttpResponse.json(pageOf(rows))
    }),
    http.get(`${API}/api/booking-requests/:id`, ({ params }) => {
      if (params.id === '403')
        return HttpResponse.json(
          { title: 'You can only view your own requests', status: 403, traceId: 'trace-403' },
          { status: 403 },
        )
      const detail = BOOKING_REQUEST_DETAILS.find((r) => String(r.id) === params.id)
      return detail
        ? HttpResponse.json(detail)
        : HttpResponse.json({ title: 'Not Found', status: 404, traceId: 'trace-404' }, { status: 404 })
    }),
  )
  return { listRequests }
}
