import { Alert, Button, type SxProps, type Theme } from '@mui/material'
import { FORBIDDEN_TEXT, isForbidden } from '../api/forbidden'
import { parseProblem } from '../api/problem'

interface Props {
  error: unknown
  /** What failed to load, as it reads after "Could not load" ("the emails", "users"). */
  what: string
  onRetry: () => void
  sx?: SxProps<Theme>
}

/**
 * The §12 error state of a data view: "Could not load {what}: {title}" with Retry ("Cannot reach the server" when the
 * API is unreachable). A 403 shows "You don't have access to this." in place, without Retry (retrying cannot help) and
 * without navigating; only a page's main query redirects (MAIN_QUERY_META).
 */
export function QueryErrorAlert({ error, what, onRetry, sx }: Props) {
  if (isForbidden(error))
    return (
      <Alert severity="warning" sx={sx}>
        {FORBIDDEN_TEXT}
      </Alert>
    )
  return (
    <Alert
      severity="error"
      sx={sx}
      action={
        <Button color="inherit" size="small" onClick={onRetry}>
          Retry
        </Button>
      }
    >
      Could not load {what}: {parseProblem(error).title}
    </Alert>
  )
}
