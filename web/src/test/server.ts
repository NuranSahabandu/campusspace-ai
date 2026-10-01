import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { EMPTY_DASHBOARD } from '../features/reports/emptyReports'

export const API = 'http://localhost:5080'

/**
 * Tests add their own handlers with server.use(...). The one default: a Facilities Officer lands on the dashboard (/),
 * so every test that signs in as an officer at / gets an empty dashboard unless it overrides it (reportsHandlers).
 */
export const server = setupServer(http.get(`${API}/api/reports/dashboard`, () => HttpResponse.json(EMPTY_DASHBOARD)))
