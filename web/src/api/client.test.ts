import { http, HttpResponse } from 'msw'
import { useAuthStore } from '../auth/authStore'
import { Roles } from '../auth/roles'
import { makeAuth } from '../test/fixtures'
import { API, server } from '../test/server'
import { api, browser, LOGIN_PATH } from './client'

describe('api client', () => {
  it('sends the Bearer token', async () => {
    useAuthStore.getState().login(makeAuth(Roles.Admin))
    let auth: string | null = null
    server.use(
      http.get(`${API}/api/auth/me`, ({ request }) => {
        auth = request.headers.get('Authorization')
        return HttpResponse.json({})
      }),
    )

    await api.get('/api/auth/me')

    expect(auth).toBe('Bearer token-Admin')
  })

  it('clears the session and redirects on a 401 from a normal call', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {})
    useAuthStore.getState().login(makeAuth(Roles.Admin))
    server.use(http.get(`${API}/api/users`, () => HttpResponse.json({ status: 401 }, { status: 401 })))

    await expect(api.get('/api/users')).rejects.toThrow()

    expect(useAuthStore.getState().token).toBeNull()
    expect(assign).toHaveBeenCalledWith('/login?expired=1')
  })

  it('does not log out on a 401 from login', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {})
    server.use(http.post(`${API}${LOGIN_PATH}`, () => HttpResponse.json({ status: 401 }, { status: 401 })))

    await expect(api.post(LOGIN_PATH, {})).rejects.toThrow()

    expect(assign).not.toHaveBeenCalled()
  })
})
