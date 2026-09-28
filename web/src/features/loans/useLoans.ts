import { useCallback } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { type ListParams, usePagedQuery } from '../../api/list'
import type { LoanDto } from '../../api/types'

/** GET /api/loans filters. overdue true: out and past due; false: returned or not yet due; absent: all. */
export interface LoansParams extends ListParams {
  overdue?: boolean
}

export const loansKeys = {
  all: ['loans'] as const,
  list: (params: LoansParams) => [...loansKeys.all, 'list', params] as const,
  detail: (id: number) => [...loansKeys.all, 'detail', id] as const,
  photo: (id: number) => [...loansKeys.all, 'photo', id] as const,
}

/** Grid column field → the server sort field (LoansQuery.SortFields). */
export const LOANS_SORT_FIELDS: Record<string, string> = { dueAt: 'dueAt' }

export function useLoans(params: LoansParams) {
  return usePagedQuery<LoanDto>(loansKeys.list(params), '/api/loans', params)
}

export function useLoan(id: number) {
  return useQuery({
    queryKey: loansKeys.detail(id),
    queryFn: async ({ signal }) => (await api.get<LoanDto>(`/api/loans/${id}`, { signal })).data,
  })
}

/**
 * A loan's damage photo as a Blob. It goes through `api`, so it carries the Bearer token; a bare <img src> to the API
 * would not, and the route is not public.
 */
export function useLoanPhoto(id: number, enabled: boolean) {
  return useQuery({
    queryKey: loansKeys.photo(id),
    queryFn: async ({ signal }) => (await api.get<Blob>(`/api/loans/${id}/photo`, { responseType: 'blob', signal })).data,
    enabled,
    // The photo of a checked-in loan never changes.
    staleTime: Infinity,
  })
}

/**
 * A ref for an <img> that shows a Blob through an object URL. React 19 runs the returned cleanup when the Blob changes
 * or the image unmounts, which revokes the URL, so no object URL outlives the image.
 */
export function useBlobImageRef(blob: Blob | undefined) {
  return useCallback(
    (img: HTMLImageElement | null) => {
      if (!img || !blob) return
      const url = URL.createObjectURL(blob)
      img.src = url
      return () => URL.revokeObjectURL(url)
    },
    [blob],
  )
}
