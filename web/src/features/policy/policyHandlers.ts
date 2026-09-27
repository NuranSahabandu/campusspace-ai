import { http, HttpResponse } from 'msw'
import type { PolicySettingDto, PolicySettingsUpdateRequest } from '../../api/types'
import { POLICY_SETTINGS } from '../../test/fixtures'
import { API, server } from '../../test/server'

/**
 * GET and PUT /api/policy-settings. The PUT records its bodies and, by default, answers with the settings the body
 * produces (changed rows updated by "Mr. Perera"), like the real endpoint.
 */
export function policyHandlers(put?: (body: PolicySettingsUpdateRequest) => Response) {
  const bodies: PolicySettingsUpdateRequest[] = []
  let settings: PolicySettingDto[] = POLICY_SETTINGS
  server.use(
    http.get(`${API}/api/policy-settings`, () => HttpResponse.json(settings)),
    http.put(`${API}/api/policy-settings`, async ({ request }) => {
      const body = (await request.json()) as PolicySettingsUpdateRequest
      bodies.push(body)
      if (put) return put(body)
      settings = settings.map((s) => {
        const change = body.settings.find((c) => c.key === s.key)
        return change ? { ...s, value: change.value, updatedAt: '2026-09-27T06:00:00Z', updatedByName: 'Mr. Perera' } : s
      })
      return HttpResponse.json(settings)
    }),
  )
  return { bodies }
}
