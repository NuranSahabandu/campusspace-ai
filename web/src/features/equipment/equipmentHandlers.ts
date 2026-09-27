import { http, HttpResponse } from 'msw'
import { EQUIPMENT_CATEGORIES, EQUIPMENT_ITEMS, EQUIPMENT_TYPES, FEATURES, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'

/**
 * The read endpoints both equipment pages use. Returns the recorded list queries: typeRequests excludes the
 * pageSize=100 options request that fills the type pickers.
 */
export function equipmentHandlers() {
  const typeRequests: URLSearchParams[] = []
  const itemRequests: URLSearchParams[] = []
  server.use(
    http.get(`${API}/api/features`, () => HttpResponse.json(FEATURES)),
    http.get(`${API}/api/equipment-types/categories`, () => HttpResponse.json(EQUIPMENT_CATEGORIES)),
    http.get(`${API}/api/equipment-types`, ({ request }) => {
      const params = new URL(request.url).searchParams
      if (params.get('pageSize') !== '100') typeRequests.push(params)
      return HttpResponse.json(pageOf(EQUIPMENT_TYPES))
    }),
    http.get(`${API}/api/equipment-items`, ({ request }) => {
      itemRequests.push(new URL(request.url).searchParams)
      return HttpResponse.json(pageOf(EQUIPMENT_ITEMS))
    }),
  )
  return { typeRequests, itemRequests }
}
