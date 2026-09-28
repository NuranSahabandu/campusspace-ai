import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { TextField } from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import { parseProblem } from '../../api/problem'
import type { BookingRequestDetailDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { roomsKeys } from '../facilities/useFacilities'
import { bookingRequestsKeys } from './useRequests'

/** CancelBookingRequestRequest.Reason's limit (BookingRequestConfiguration.CancelReasonMaxLength). */
export const CANCEL_REASON_MAX = 500

// An officer must give a reason (the server returns 400 on Reason otherwise), so the web always asks for one.
const schema = z.object({
  reason: z.string().trim().min(1, 'A reason is required').max(CANCEL_REASON_MAX, `At most ${CANCEL_REASON_MAX} characters`),
})

type CancelForm = z.infer<typeof schema>

// What a cancellation changes: the request (list and detail) and, for an Approved one, the booking, which leaves the
// blackout clash counts and clash lists.
const INVALIDATE = [bookingRequestsKeys.all, roomsKeys.all]

interface Props {
  requestId: number
  /** What is being cancelled, for example the booking time and the requester. */
  message: string
  /** The reason to start from (editable), for example "Room unavailable: <blackout reason>". */
  defaultReason?: string
  onClose: () => void
}

/**
 * An officer cancels a booking request with a required reason (POST /api/booking-requests/{id}/cancel). Mount it only
 * while open, so each opening starts from defaultReason. A 409 (for example "The booking has already started") is shown
 * exactly as sent; the dialog then closes and the lists reload, since the request changed under us.
 */
export function CancelRequestDialog({ requestId, message, defaultReason = '', onClose }: Props) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, setError, control, formState: { errors } } = useForm<CancelForm>({
    resolver: zodResolver(schema),
    defaultValues: { reason: defaultReason.slice(0, CANCEL_REASON_MAX) },
  })
  const mutation = useApiMutation<CancelForm, BookingRequestDetailDto, CancelForm>({
    mutationFn: async ({ reason }) =>
      (await api.post<BookingRequestDetailDto>(`/api/booking-requests/${requestId}/cancel`, { reason })).data,
    invalidate: INVALIDATE,
    successMessage: 'Request cancelled',
    form: { setError, fields: ['reason'] },
    onSuccess: onClose,
  })

  const submit = handleSubmit((values) =>
    mutation.mutate(values, {
      onError: (error) => {
        if (parseProblem(error).status !== 409) return
        INVALIDATE.forEach((queryKey) => queryClient.invalidateQueries({ queryKey }))
        onClose()
      },
    }),
  )

  const length = useWatch({ control, name: 'reason' }).length

  return (
    <ConfirmDialog
      open
      title="Cancel request?"
      message={message}
      confirmLabel="Cancel request"
      cancelLabel="Keep request"
      destructive
      pending={mutation.isPending}
      onConfirm={() => void submit()}
      onClose={onClose}
    >
      <TextField
        label="Reason"
        {...register('reason')}
        error={!!errors.reason}
        helperText={errors.reason?.message ?? `Saved in the request's history · ${length}/${CANCEL_REASON_MAX}`}
        multiline
        minRows={2}
        fullWidth
        required
        sx={{ mt: 2 }}
        slotProps={{ htmlInput: { maxLength: CANCEL_REASON_MAX } }}
      />
    </ConfirmDialog>
  )
}
