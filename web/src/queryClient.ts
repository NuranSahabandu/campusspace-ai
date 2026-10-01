import { QueryCache, QueryClient } from '@tanstack/react-query'
import { browser } from './api/client'
import { FORBIDDEN_PATH, isForbidden, isPermanentClientError } from './api/forbidden'

/** TanStack Query owns all server state (ADR-1). */
export function createQueryClient(overrides?: { retry?: number | false }) {
  return new QueryClient({
    // A 403 on a page's main query (MAIN_QUERY_META) is an access-denied page, like a 401 is the login page.
    queryCache: new QueryCache({
      onError: (error, query) => {
        if (query.meta?.forbiddenRedirect && isForbidden(error)) browser.assign(FORBIDDEN_PATH)
      },
    }),
    defaultOptions: {
      queries: {
        // Once for network errors and 5xx; never for a 4xx that would fail again (a 403 or 404 shows at once).
        retry: overrides?.retry ?? ((failures, error) => failures < 1 && !isPermanentClientError(error)),
        staleTime: 30_000,
        refetchOnWindowFocus: false,
      },
      mutations: { retry: false },
    },
  })
}
