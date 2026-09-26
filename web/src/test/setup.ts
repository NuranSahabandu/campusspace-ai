import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { useAuthStore } from '../auth/authStore'
import { useToastStore } from '../ui/toastStore'
import { server } from './server'

// findBy*/waitFor give up after 1 s by default. MUI DataGrid rendering is slower on CI runners
// (ClubsPage took about 1.3 s there), so allow 5 s before an async query fails.
configure({ asyncUtilTimeout: 5000 })

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
