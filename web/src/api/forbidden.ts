import axios from 'axios'

/**
 * Query `meta` for a page's MAIN query (its list, detail, report or settings): a 403 on it sends the user to the
 * access-denied page (plan §12), handled once in the QueryCache (`queryClient.ts`). Secondary widgets and polls leave it
 * out and show QueryErrorAlert's in-place "You don't have access to this." instead; mutation 403s stay in their dialogs.
 * Do not use it for a query whose 403 is an object-level check that must not reveal the row exists (booking request
 * detail shows "Request not found").
 */
export const MAIN_QUERY_META = { forbiddenRedirect: true } as const

export const FORBIDDEN_PATH = '/forbidden'

/** The in-place text for a 403 on a secondary widget. */
export const FORBIDDEN_TEXT = "You don't have access to this."

export const isForbidden = (error: unknown) => axios.isAxiosError(error) && error.response?.status === 403

/** A 4xx other than 408/429: the same request will fail the same way, so a query does not retry it. */
export const isPermanentClientError = (error: unknown) => {
  const status = axios.isAxiosError(error) ? error.response?.status : undefined
  return status !== undefined && status >= 400 && status < 500 && status !== 408 && status !== 429
}
