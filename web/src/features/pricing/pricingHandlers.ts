import { http, HttpResponse } from 'msw'
import { CURRENT_PRICES, pageOf, PRICING_RULES } from '../../test/fixtures'
import { API, server } from '../../test/server'

/** The read endpoints the pricing page uses. Returns the recorded list queries and the count of /current calls. */
export function pricingHandlers() {
  const listRequests: URLSearchParams[] = []
  const calls = { current: 0 }
  server.use(
    http.get(`${API}/api/pricing-rules/current`, () => {
      calls.current += 1
      return HttpResponse.json(CURRENT_PRICES)
    }),
    http.get(`${API}/api/pricing-rules`, ({ request }) => {
      listRequests.push(new URL(request.url).searchParams)
      return HttpResponse.json(pageOf(PRICING_RULES))
    }),
  )
  return { listRequests, calls }
}
