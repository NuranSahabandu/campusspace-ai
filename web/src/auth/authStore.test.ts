import { Roles } from './roles'
import { AUTH_STORAGE_KEY, useAuthStore } from './authStore'
import { makeAuth } from '../test/fixtures'

const persisted = (expiresAt: string) =>
  JSON.stringify({ state: { token: 't', expiresAt, user: makeAuth(Roles.Admin).user }, version: 0 })

describe('authStore', () => {
  afterEach(() => vi.useRealTimers())

  it('drops an expired session on load', async () => {
    localStorage.setItem(AUTH_STORAGE_KEY, persisted(new Date(Date.now() - 1000).toISOString()))

    await useAuthStore.persist.rehydrate()

    expect(useAuthStore.getState().token).toBeNull()
  })

  it('keeps a valid session on load', async () => {
    localStorage.setItem(AUTH_STORAGE_KEY, persisted(new Date(Date.now() + 60_000).toISOString()))

    await useAuthStore.persist.rehydrate()

    expect(useAuthStore.getState().token).toBe('t')
  })

  it('logs out automatically at expiresAt', () => {
    vi.useFakeTimers()
    useAuthStore.getState().login({ ...makeAuth(Roles.Admin), expiresAt: new Date(Date.now() + 5_000).toISOString() })

    vi.advanceTimersByTime(4_999)
    expect(useAuthStore.getState().token).not.toBeNull()
    vi.advanceTimersByTime(1)
    expect(useAuthStore.getState().token).toBeNull()
  })
})
