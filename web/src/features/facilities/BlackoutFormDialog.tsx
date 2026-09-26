import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import type { BlackoutDto, RoomDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { campusLocalToIso } from '../../ui/formatDateTime'
import { roomsKeys } from './useFacilities'

// Mirrors CreateBlackoutRequest. Both values are datetime-local strings (YYYY-MM-DDTHH:mm) in campus time,
// so comparing them as strings compares the instants.
const schema = z
  .object({
    start: z.string().min(1, 'Start is required'),
    end: z.string().min(1, 'End is required'),
    reason: z.string().trim().min(1, 'Reason is required').max(200, 'At most 200 characters'),
  })
  .refine((v) => !v.start || !v.end || v.end > v.start, { message: 'End must be after start', path: ['end'] })

type BlackoutForm = z.infer<typeof schema>

/** Adds a maintenance blackout [start, end) to a room (UC14). Times are entered in campus time. */
export function BlackoutFormDialog({ room, onClose }: { room: RoomDto; onClose: () => void }) {
  const { register, handleSubmit, setError, formState: { errors } } = useForm<BlackoutForm>({
    resolver: zodResolver(schema),
    defaultValues: { start: '', end: '', reason: '' },
  })
  const mutation = useApiMutation<BlackoutForm, BlackoutDto, BlackoutForm>({
    mutationFn: async ({ start, end, reason }) =>
      (
        await api.post<BlackoutDto>(`/api/rooms/${room.id}/blackouts`, {
          start: campusLocalToIso(start),
          end: campusLocalToIso(end),
          reason,
        })
      ).data,
    invalidate: [roomsKeys.all],
    successMessage: 'Blackout added',
    form: { setError, fields: ['start', 'end', 'reason'] },
    onSuccess: onClose,
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-labelledby="blackout-form-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="blackout-form-title">Add blackout to {room.code}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <TextField
              label="Start"
              type="datetime-local"
              {...register('start')}
              error={!!errors.start}
              helperText={errors.start?.message ?? 'Campus time (Asia/Colombo)'}
              slotProps={{ inputLabel: { shrink: true } }}
            />
            <TextField
              label="End"
              type="datetime-local"
              {...register('end')}
              error={!!errors.end}
              helperText={errors.end?.message ?? 'Not included: the room is free again from this time'}
              slotProps={{ inputLabel: { shrink: true } }}
            />
            <TextField
              label="Reason"
              {...register('reason')}
              error={!!errors.reason}
              helperText={errors.reason?.message}
              multiline
              minRows={2}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            Add
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
