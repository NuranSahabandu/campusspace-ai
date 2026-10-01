import { http, HttpResponse } from 'msw'
import type { AgentRunDetailDto, AgentRunListItemDto, AgentRunMetricsDto, PagedResult } from '../../api/types'
import { API, server } from '../../test/server'
import { FAILED_RUN_DETAIL, METRICS, RUNS_PAGE } from './agentRunsFixtures'

interface Options {
  metrics?: AgentRunMetricsDto
  /** A status other than 200 answers the metrics with Problem Details. */
  metricsStatus?: number
  page?: PagedResult<AgentRunListItemDto>
  detail?: AgentRunDetailDto
}

/** Every request the monitor made, as URLSearchParams, newest last. */
export interface Requests {
  list: URLSearchParams[]
  metrics: URLSearchParams[]
}

/**
 * The monitor's endpoints over the captured responses (agentRunsFixtures.ts). The detail answers any run id with
 * `detail`, except the id 00000000-0000-0000-0000-000000000404, which is a 404.
 */
export function agentRunsHandlers(options: Options = {}): Requests {
  const requests: Requests = { list: [], metrics: [] }
  server.use(
    http.get(`${API}/api/agent-runs/metrics`, ({ request }) => {
      requests.metrics.push(new URL(request.url).searchParams)
      const status = options.metricsStatus ?? 200
      if (status !== 200)
        return HttpResponse.json({ title: 'Metrics are unavailable', status, traceId: 't-1' }, { status })
      return HttpResponse.json(options.metrics ?? METRICS)
    }),
    http.get(`${API}/api/agent-runs/:id`, ({ params }) =>
      params.id === '00000000-0000-0000-0000-000000000404'
        ? HttpResponse.json({ title: 'Not Found', status: 404, traceId: 't-2' }, { status: 404 })
        : HttpResponse.json({ ...(options.detail ?? FAILED_RUN_DETAIL), id: String(params.id) }),
    ),
    http.get(`${API}/api/agent-runs`, ({ request }) => {
      requests.list.push(new URL(request.url).searchParams)
      return HttpResponse.json(options.page ?? RUNS_PAGE)
    }),
  )
  return requests
}
