import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Stack,
  TextField,
} from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import type { BlackoutClashDto, BlackoutWithClashesDto, RoomDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { campusLocalToIso, formatCampusTimeRange } from '../../ui/formatDateTime'
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

/**
 * Adds a maintenance blackout [start, end) to a room (UC14). Times are entered in campus time. When the blackout
 * overlaps active bookings, the dialog stays open and lists them: they are not cancelled, the officer handles them.
 */
export function BlackoutFormDialog({ room, onClose }: { room: RoomDto; onClose: () => void }) {
  const [clashes, setClashes] = useState<BlackoutClashDto[] | null>(null)
  const { register, handleSubmit, setError, formState: { errors } } = useForm<BlackoutForm>({
    resolver: zodResolver(schema),
    defaultValues: { start: '', end: '', reason: '' },
  })
  const mutation = useApiMutation<BlackoutForm, BlackoutWithClashesDto, BlackoutForm>({
    mutationFn: async ({ start, end, reason }) =>
      (
        await api.post<BlackoutWithClashesDto>(`/api/rooms/${room.id}/blackouts`, {
          start: campusLocalToIso(start),
          end: campusLocalToIso(end),
          reason,
        })
      ).data,
    // Runs before onSuccess in both cases, so the new blackout shows in the list behind a clash warning too.
    invalidate: [roomsKeys.all],
    successMessage: 'Blackout added',
    form: { setError, fields: ['start', 'end', 'reason'] },
    onSuccess: (blackout) => (blackout.clashes.length > 0 ? setClashes(blackout.clashes) : onClose()),
  })

  if (clashes) {
    return (
      <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="blackout-form-title">
        <DialogTitle id="blackout-form-title">Add blackout to {room.code}</DialogTitle>
        <DialogContent>
          <Alert severity="warning" sx={{ mt: 1 }}>
            Blackout added. It clashes with {clashes.length} active booking{clashes.length === 1 ? '' : 's'}; they were
            not cancelled.
          </Alert>
          {/* Names and emails are plain React text children, never HTML. */}
          <List dense aria-label="Clashing bookings">
            {clashes.map((c) => (
              <ListItem key={c.bookingId} disableGutters>
                <ListItemText
                  primary={`${formatCampusTimeRange(c.start, c.end)} · ${c.status}`}
                  secondary={`${c.requesterName} (${c.requesterEmail}) · request #${c.requestId}`}
                />
              </ListItem>
            ))}
          </List>
        </DialogContent>
        <DialogActions>
          <Button variant="contained" onClick={onClose}>
            Close
          </Button>
        </DialogActions>
      </Dialog>
    )
  }

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
