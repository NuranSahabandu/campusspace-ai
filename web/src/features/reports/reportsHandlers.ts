import { http, HttpResponse, type JsonBodyType } from 'msw'
import type { DashboardDto, DemandReportDto, UtilizationReportDto } from '../../api/types'
import { API, server } from '../../test/server'
import { DASHBOARD, DEMAND, UTILIZATION } from './reportsFixtures'

interface Options {
  dashboard?: DashboardDto
  utilization?: UtilizationReportDto
  demand?: DemandReportDto
  /** A status other than 200 answers that endpoint with Problem Details. */
  status?: Partial<Record<'dashboard' | 'utilization' | 'demand', number>>
}

/** Every request made, as URLSearchParams, newest last. */
export interface Requests {
  dashboard: number
  utilization: URLSearchParams[]
  demand: URLSearchParams[]
}

/** The report endpoints over the captured responses (reportsFixtures.ts). */
export function reportsHandlers(options: Options = {}): Requests {
  const requests: Requests = { dashboard: 0, utilization: [], demand: [] }
  const answer = (name: keyof NonNullable<Options['status']>, body: JsonBodyType) => {
    const status = options.status?.[name] ?? 200
    return status === 200
      ? HttpResponse.json(body)
      : HttpResponse.json({ title: 'Something went wrong', status, traceId: 't-1' }, { status })
  }
  server.use(
    http.get(`${API}/api/reports/dashboard`, () => {
      requests.dashboard++
      return answer('dashboard', options.dashboard ?? DASHBOARD)
    }),
    http.get(`${API}/api/reports/utilization`, ({ request }) => {
      requests.utilization.push(new URL(request.url).searchParams)
      return answer('utilization', options.utilization ?? UTILIZATION)
    }),
    http.get(`${API}/api/reports/demand`, ({ request }) => {
      requests.demand.push(new URL(request.url).searchParams)
      return answer('demand', options.demand ?? DEMAND)
    }),
  )
  return requests
}
