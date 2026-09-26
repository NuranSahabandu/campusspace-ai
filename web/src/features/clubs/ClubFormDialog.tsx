import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Stack,
  Switch,
  TextField,
} from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import type { ClubDetailDto, ClubDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { clubsKeys } from './useClubs'

// Mirrors CreateClubRequest / UpdateClubRequest; the server remains the real validator (§12).
const schema = z.object({
  name: z.string().trim().min(1, 'Name is required').max(100, 'At most 100 characters'),
  isActive: z.boolean(),
})

type ClubForm = z.infer<typeof schema>

/** New club when `club` is omitted, otherwise Edit (name and active). Clubs are never deleted. */
export function ClubFormDialog({ club, onClose }: { club?: ClubDto; onClose: () => void }) {
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<ClubForm>({
    resolver: zodResolver(schema),
    defaultValues: { name: club?.name ?? '', isActive: club?.isActive ?? true },
  })
  const mutation = useApiMutation<ClubForm, ClubDetailDto, ClubForm>({
    mutationFn: async ({ name, isActive }) =>
      club
        ? (await api.put<ClubDetailDto>(`/api/clubs/${club.id}`, { name, isActive })).data
        : (await api.post<ClubDetailDto>('/api/clubs', { name })).data,
    invalidate: [clubsKeys.all],
    successMessage: club ? 'Club updated' : 'Club created',
    form: { setError, fields: ['name', 'isActive'] },
    conflictField: 'name',
    onSuccess: onClose,
  })
  const title = club ? `Edit ${club.name}` : 'New club'

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-labelledby="club-form-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="club-form-title">{title}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <TextField label="Name" {...register('name')} error={!!errors.name} helperText={errors.name?.message} />
            {club && (
              <Controller
                name="isActive"
                control={control}
                render={({ field }) => (
                  <FormControlLabel
                    label="Active"
                    control={<Switch checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                  />
                )}
              />
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            {club ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
