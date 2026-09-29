import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, TextField } from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import { useApiMutation } from '../../api/useApiMutation'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { bookingRequestsKeys } from '../requests/useRequests'
import { approvalsKeys } from './useApprovals'

/** ApprovalDecisionConfiguration.CommentMaxLength: the most an approve comment, reason or revise notes may hold. */
export const DECISION_TEXT_MAX = 1000

export type DecisionMode = 'approve' | 'reject' | 'revise'

type Field = 'comment' | 'reason' | 'notes'

const MODES: Record<
  DecisionMode,
  { title: string; message: string; confirm: string; path: string; field: Field; label: string; required?: string; destructive?: boolean }
> = {
  approve: {
    title: 'Approve this proposal?',
    message: 'This books the room, reserves the equipment and issues the quotation.',
    confirm: 'Approve',
    path: 'approve',
    field: 'comment',
    label: 'Comment (optional)',
  },
  reject: {
    title: 'Reject this request?',
    message: 'The request is closed. The requester sees your reason.',
    confirm: 'Reject',
    path: 'reject',
    field: 'reason',
    label: 'Reason',
    required: 'A reason is required',
    destructive: true,
  },
  revise: {
    title: 'Request a revision?',
    message: 'The agents prepare a new proposal from your notes. The requester sees them.',
    confirm: 'Request revision',
    path: 'request-revision',
    field: 'notes',
    label: 'Notes for the agents',
    required: 'Notes are required',
  },
}

const text = (required?: string) => {
  const base = z.string().trim().max(DECISION_TEXT_MAX, `At most ${DECISION_TEXT_MAX} characters`)
  return required ? base.min(1, required) : base
}

// Every mode has the same form shape; only its own field is validated (the others stay empty).
const schemaFor = (mode: DecisionMode) =>
  z.object({
    comment: mode === 'approve' ? text() : z.string(),
    reason: mode === 'reject' ? text(MODES.reject.required) : z.string(),
    notes: mode === 'revise' ? text(MODES.revise.required) : z.string(),
  })

type DecisionForm = z.infer<ReturnType<typeof schemaFor>>

/** A decision's HTTP status: approve answers 200 (Approved) or 202 (ApprovalInProgress); revise 202; reject 200. */
export interface DecisionResult {
  mode: DecisionMode
  status: number
}

interface Props {
  mode: DecisionMode
  requestId: number
  onClose: () => void
  onDone: (result: DecisionResult) => void
  /** A 409 (the proposal is no longer valid, the time is no longer valid, already being decided): the server's message. */
  onConflict: (message: string) => void
}

/**
 * Approve (optional comment), Reject (reason required) or Request revision (notes required) through ConfirmDialog,
 * with RHF + Zod mirroring the server (≤ 1000 characters, blank is invalid). The server is the real validator.
 */
export function DecisionDialog({ mode, requestId, onClose, onDone, onConflict }: Props) {
  const config = MODES[mode]
  const {
    register,
    handleSubmit,
    setError,
    control,
    formState: { errors },
  } = useForm<DecisionForm>({
    resolver: zodResolver(schemaFor(mode)),
    defaultValues: { comment: '', reason: '', notes: '' },
  })

  const mutation = useApiMutation<DecisionForm, DecisionResult, DecisionForm>({
    mutationFn: async (values) => {
      const value = values[config.field]
      const response = await api.post(`/api/booking-requests/${requestId}/${config.path}`, {
        [config.field]: value === '' ? null : value,
      })
      return { mode, status: response.status }
    },
    invalidate: [bookingRequestsKeys.all, approvalsKeys.all],
    form: { setError, fields: [config.field] },
    onConflict: (message) => {
      onConflict(message)
      onClose()
    },
    onSuccess: (result) => {
      onDone(result)
      onClose()
    },
  })

  const length = useWatch({ control, name: config.field }).length
  const error = errors[config.field]

  return (
    <ConfirmDialog
      open
      title={config.title}
      message={config.message}
      confirmLabel={config.confirm}
      destructive={config.destructive}
      pending={mutation.isPending}
      onConfirm={() => void handleSubmit((values) => mutation.mutate(values))()}
      onClose={onClose}
    >
      {errors.root?.server && (
        <Alert severity="error" sx={{ mt: 2 }}>
          {errors.root.server.message}
        </Alert>
      )}
      <TextField
        label={config.label}
        {...register(config.field)}
        error={!!error}
        helperText={error?.message ?? `${length}/${DECISION_TEXT_MAX}`}
        multiline
        minRows={3}
        fullWidth
        required={!!config.required}
        sx={{ mt: 2 }}
        slotProps={{ htmlInput: { maxLength: DECISION_TEXT_MAX } }}
      />
    </ConfirmDialog>
  )
}
