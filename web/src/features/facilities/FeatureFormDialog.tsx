import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import type { FeatureDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { featuresKeys, roomsKeys } from './useFacilities'

export const FEATURE_CODE_HINT = 'lowercase_snake_case, used by the AI agents'

// Mirrors FeatureRequest. The server lower-cases the code, so check it lower-cased.
const schema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Code is required')
    .max(50, 'At most 50 characters')
    .refine((v) => /^[a-z][a-z0-9]*(_[a-z0-9]+)*$/.test(v.toLowerCase()), 'Use snake_case, for example sound_system'),
  name: z.string().trim().min(1, 'Name is required').max(100, 'At most 100 characters'),
})

type FeatureForm = z.infer<typeof schema>

/** New feature when `feature` is omitted, otherwise Edit. A code that rooms still use cannot be renamed (409). */
export function FeatureFormDialog({ feature, onClose }: { feature?: FeatureDto; onClose: () => void }) {
  const { register, handleSubmit, setError, formState: { errors } } = useForm<FeatureForm>({
    resolver: zodResolver(schema),
    defaultValues: { code: feature?.code ?? '', name: feature?.name ?? '' },
  })
  const mutation = useApiMutation<FeatureForm, FeatureDto, FeatureForm>({
    mutationFn: async (values) =>
      feature
        ? (await api.put<FeatureDto>(`/api/features/${feature.id}`, values)).data
        : (await api.post<FeatureDto>('/api/features', values)).data,
    invalidate: [featuresKeys.all, roomsKeys.all],
    successMessage: feature ? 'Feature updated' : 'Feature created',
    form: { setError, fields: ['code', 'name'] },
    conflictField: 'code',
    onSuccess: onClose,
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-labelledby="feature-form-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="feature-form-title">{feature ? `Edit ${feature.code}` : 'New feature'}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <TextField
              label="Code"
              {...register('code')}
              error={!!errors.code}
              helperText={errors.code?.message ?? FEATURE_CODE_HINT}
            />
            <TextField label="Name" {...register('name')} error={!!errors.name} helperText={errors.name?.message} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            {feature ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
