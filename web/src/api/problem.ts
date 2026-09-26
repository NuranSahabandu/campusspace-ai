import axios from 'axios'
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'

/** RFC 9457 Problem Details, reduced to what the UI needs. */
export interface Problem {
  title: string
  status: number | null
  traceId: string | null
  /** camelCase field name → messages. */
  fieldErrors: Record<string, string[]>
}

// ASP.NET ModelState keys can arrive as "Email" or "$.email"; forms use camelCase names.
const toCamel = (key: string) => {
  const name = key.replace(/^\$\./, '')
  return name.charAt(0).toLowerCase() + name.slice(1)
}

export function parseProblem(error: unknown): Problem {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as Record<string, unknown> | undefined
    const errors = (body?.errors ?? {}) as Record<string, string[]>
    return {
      title: typeof body?.title === 'string' ? body.title : error.response ? 'Request failed' : 'Cannot reach the server',
      status: error.response?.status ?? null,
      traceId: typeof body?.traceId === 'string' ? body.traceId : null,
      fieldErrors: Object.fromEntries(Object.entries(errors).map(([k, v]) => [toCamel(k), v])),
    }
  }
  return { title: 'Something went wrong', status: null, traceId: null, fieldErrors: {} }
}

/**
 * Shows server field errors on the matching form fields. The server is the real validator (§12).
 * Returns true if at least one error matched a form field.
 */
export function applyFieldErrors<T extends FieldValues>(
  fieldErrors: Record<string, string[]>,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
): boolean {
  let applied = false
  for (const [key, messages] of Object.entries(fieldErrors)) {
    if ((fields as readonly string[]).includes(key)) {
      setError(key as Path<T>, { type: 'server', message: messages.join(' ') })
      applied = true
    }
  }
  return applied
}
