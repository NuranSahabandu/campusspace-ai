import { QueryClient } from '@tanstack/react-query'

/** TanStack Query owns all server state (ADR-1). */
export function createQueryClient(overrides?: { retry?: number | false }) {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: overrides?.retry ?? 1,
        staleTime: 30_000,
        refetchOnWindowFocus: false,
      },
      mutations: { retry: false },
    },
  })
}
