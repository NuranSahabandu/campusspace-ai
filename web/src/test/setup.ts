import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { useAuthStore } from '../auth/authStore'
import { useToastStore } from '../ui/toastStore'
import { server } from './server'

// No real network: any request without a handler fails the test.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  cleanup()
  server.resetHandlers()
  useAuthStore.getState().logout()
  useToastStore.getState().dismiss()
  localStorage.clear()
  vi.restoreAllMocks()
})
afterAll(() => server.close())
