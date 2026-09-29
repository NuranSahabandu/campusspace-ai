import { type QueryKey, useMutation, useQueryClient } from '@tanstack/react-query'
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { toast } from '../ui/toastStore'
import { applyFieldErrors, parseProblem } from './problem'

interface Options<TVars, TData, TForm extends FieldValues> {
  mutationFn: (vars: TVars) => Promise<TData>
  /** Query keys to refetch after success. */
  invalidate?: QueryKey[]
  successMessage?: string
  /** Where 400 field errors go. Unmatched ones land on root.server, for an Alert in the dialog. */
  form?: { setError: UseFormSetError<TForm>; fields: readonly Path<TForm>[] }
  /** The form field a 409 message belongs to (for example 'email'). Without it a 409 is a toast. */
  conflictField?: Path<TForm>
  /** Toast text for a 409 that has no conflictField (for example a delete of a row still in use). */
  conflictMessage?: string
  /**
   * Handles a 409 itself instead of a toast, with the server's message exactly as sent (Problem Details title), for
   * example a page-level alert that stays after its dialog closes.
   */
  onConflict?: (message: string) => void
  onSuccess?: (data: TData, vars: TVars) => void
}

/** The toast text for an unexpected error. The traceId lets the team find the server log (§9). */
export const errorToastMessage = (error: unknown) => {
  const { title, traceId } = parseProblem(error)
  return traceId ? `${title} (trace ${traceId})` : title
}

/**
 * useMutation with the app's standard feedback (§12): success → toast and refetch; 400 → field errors on the form;
 * 409 → the conflict field or a toast; anything else → an error toast with the traceId.
 */
export function useApiMutation<TVars = void, TData = unknown, TForm extends FieldValues = FieldValues>({
  mutationFn,
  invalidate = [],
  successMessage,
  form,
  conflictField,
  conflictMessage,
  onConflict,
  onSuccess,
}: Options<TVars, TData, TForm>) {
  const queryClient = useQueryClient()

  return useMutation<TData, unknown, TVars>({
    mutationFn,
    onSuccess: async (data, vars) => {
      await Promise.all(invalidate.map((queryKey) => queryClient.invalidateQueries({ queryKey })))
      if (successMessage) toast.success(successMessage)
      onSuccess?.(data, vars)
    },
    onError: (error) => {
      const problem = parseProblem(error)
      if (problem.status === 400 && form) {
        if (!applyFieldErrors(problem.fieldErrors, form.setError, form.fields)) {
          const messages = Object.values(problem.fieldErrors).flat()
          form.setError('root.server', { type: 'server', message: messages.length ? messages.join(' ') : problem.title })
        }
        return
      }
      if (problem.status === 409 && form && conflictField) {
        form.setError(conflictField, { type: 'server', message: problem.title })
        return
      }
      if (problem.status === 409 && onConflict) {
        onConflict(problem.title)
        return
      }
      if (problem.status === 409 && conflictMessage) {
        toast.error(conflictMessage)
        return
      }
      if (problem.status === 400 || problem.status === 409) {
        toast.error(Object.values(problem.fieldErrors).flat()[0] ?? problem.title)
        return
      }
      toast.error(errorToastMessage(error))
    },
  })
}
