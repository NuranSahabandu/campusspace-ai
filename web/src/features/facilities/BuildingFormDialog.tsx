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
import type { BuildingDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { buildingsKeys, roomsKeys } from './useFacilities'

// Mirrors CreateBuildingRequest / UpdateBuildingRequest. The server upper-cases the code, so check it upper-cased.
const schema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Code is required')
    .max(10, 'At most 10 characters')
    .refine((v) => /^[A-Z0-9][A-Z0-9-]*$/.test(v.toUpperCase()), 'Only letters, digits and -'),
  name: z.string().trim().min(1, 'Name is required').max(100, 'At most 100 characters'),
  isActive: z.boolean(),
})

type BuildingForm = z.infer<typeof schema>

/** New building when `building` is omitted, otherwise Edit (with the active switch). */
export function BuildingFormDialog({ building, onClose }: { building?: BuildingDto; onClose: () => void }) {
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<BuildingForm>({
    resolver: zodResolver(schema),
    defaultValues: { code: building?.code ?? '', name: building?.name ?? '', isActive: building?.isActive ?? true },
  })
  const mutation = useApiMutation<BuildingForm, BuildingDto, BuildingForm>({
    mutationFn: async ({ code, name, isActive }) =>
      building
        ? (await api.put<BuildingDto>(`/api/buildings/${building.id}`, { code, name, isActive })).data
        : (await api.post<BuildingDto>('/api/buildings', { code, name })).data,
    invalidate: [buildingsKeys.all, roomsKeys.all],
    successMessage: building ? 'Building updated' : 'Building created',
    form: { setError, fields: ['code', 'name', 'isActive'] },
    conflictField: 'code',
    onSuccess: onClose,
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-labelledby="building-form-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="building-form-title">{building ? `Edit ${building.code}` : 'New building'}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <TextField
              label="Code"
              {...register('code')}
              error={!!errors.code}
              helperText={errors.code?.message ?? 'Saved in upper case, for example MB'}
            />
            <TextField label="Name" {...register('name')} error={!!errors.name} helperText={errors.name?.message} />
            {building && (
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
            {building ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
