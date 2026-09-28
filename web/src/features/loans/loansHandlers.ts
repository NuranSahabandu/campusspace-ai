import { http, HttpResponse } from 'msw'
import type { LoanDto } from '../../api/types'
import { LOANS, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'

/** The first bytes of a PNG file: enough for a Blob the drawer shows through an object URL. */
export const PNG_BYTES = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])

/**
 * The loan endpoints the Loans page uses. Records each list query and the Authorization header of each photo request.
 * The list ignores the filters; a detail of an unknown id is 404.
 */
export function loansHandlers({ rows = LOANS }: { rows?: LoanDto[] } = {}) {
  const listRequests: URLSearchParams[] = []
  const photoAuth: (string | null)[] = []
  server.use(
    http.get(`${API}/api/loans`, ({ request }) => {
      listRequests.push(new URL(request.url).searchParams)
      return HttpResponse.json(pageOf(rows))
    }),
    http.get(`${API}/api/loans/:id`, ({ params }) => {
      const loan = rows.find((l) => String(l.id) === params.id)
      return loan ? HttpResponse.json(loan) : HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 })
    }),
    http.get(`${API}/api/loans/:id/photo`, ({ request }) => {
      photoAuth.push(request.headers.get('Authorization'))
      return new HttpResponse(PNG_BYTES, { headers: { 'Content-Type': 'image/png' } })
    }),
  )
  return { listRequests, photoAuth }
}
