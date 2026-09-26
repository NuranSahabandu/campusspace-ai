import type { ReactNode } from 'react'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { http, HttpResponse } from 'msw'
import { createQueryClient } from '../queryClient'
import { API, server } from '../test/server'
import { useToastStore } from '../ui/toastStore'
import { api } from './client'
import { useApiMutation } from './useApiMutation'

const wrapper = ({ children }: { children: ReactNode }) => (
  <QueryClientProvider client={createQueryClient({ retry: false })}>{children}</QueryClientProvider>
)

describe('useApiMutation', () => {
  it('shows a success toast', async () => {
    server.use(http.post(`${API}/api/things`, () => HttpResponse.json({}, { status: 201 })))
    const { result } = renderHook(
      () => useApiMutation({ mutationFn: () => api.post('/api/things'), successMessage: 'Saved' }),
      { wrapper },
    )

    act(() => result.current.mutate())

    await waitFor(() => expect(useToastStore.getState().current?.message).toBe('Saved'))
  })

  it('shows the traceId for an unexpected error', async () => {
    server.use(
      http.post(`${API}/api/things`, () =>
        HttpResponse.json({ status: 500, title: 'An unexpected error occurred', traceId: 'abc123' }, { status: 500 }),
      ),
    )
    const { result } = renderHook(() => useApiMutation({ mutationFn: () => api.post('/api/things') }), { wrapper })

    act(() => result.current.mutate())

    await waitFor(() =>
      expect(useToastStore.getState().current).toMatchObject({
        severity: 'error',
        message: 'An unexpected error occurred (trace abc123)',
      }),
    )
  })

  it('puts unmatched 400 errors on root.server', async () => {
    server.use(
      http.post(`${API}/api/things`, () =>
        HttpResponse.json({ status: 400, title: 'Bad', errors: { ClubId: ['Club is inactive.'] } }, { status: 400 }),
      ),
    )
    const setError = vi.fn()
    const { result } = renderHook(
      () => useApiMutation({ mutationFn: () => api.post('/api/things'), form: { setError, fields: ['name'] } }),
      { wrapper },
    )

    act(() => result.current.mutate())

    await waitFor(() =>
      expect(setError).toHaveBeenCalledWith('root.server', { type: 'server', message: 'Club is inactive.' }),
    )
  })
})
