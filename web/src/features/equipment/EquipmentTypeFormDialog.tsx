import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormHelperText,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  TextField,
} from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import type { EquipmentTypeDto, EquipmentTypeRequest } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { useFeatures } from '../facilities/useFacilities'
import { equipmentItemsKeys, equipmentTypesKeys, useEquipmentCategories } from './useEquipment'

export const TYPE_CODE_HINT = 'Upper-case letters, digits and hyphens, e.g. MIC-WIRELESS'
export const TYPE_CODE_LOCKED = "Codes can't be changed"
export const COVERED_BY_HINT = 'Leave empty unless rooms can have this built in'

const MAX_FEE = 99_999_999.99

// Mirrors EquipmentTypeRequest and the checks in EquipmentTypeService; the server remains the real validator (§12).
const schema = z.object({
  code: z
    .string()
    .trim()
    .toUpperCase()
    .min(2, 'At least 2 characters')
    .max(40, 'At most 40 characters')
    .regex(/^[A-Z0-9]+(-[A-Z0-9]+)*$/, 'Use letters and digits in hyphen-separated parts, for example MIC-WIRELESS'),
  name: z.string().trim().min(1, 'Name is required').max(100, 'At most 100 characters'),
  category: z.string().min(1, 'Choose a category'),
  // Kept as the typed text so "12.345" can be rejected rather than rounded; the first failing check wins.
  feePerBooking: z.string().superRefine((raw, ctx) => {
    const value = raw.trim()
    const fail = (message: string) => ctx.addIssue({ code: 'custom', message })
    if (value === '') return fail('Fee is required')
    const fee = Number(value)
    if (!Number.isFinite(fee)) return fail('Enter a number')
    if (fee < 0) return fail("Fee can't be negative")
    if (!/^\d+(\.\d{1,2})?$/.test(value)) return fail('Fee can have at most 2 decimal places.')
    if (fee > MAX_FEE) return fail('At most 99,999,999.99')
  }),
  // '' means none.
  coveredByFeatureCode: z.string(),
})

type TypeForm = z.infer<typeof schema>

const FIELDS = ['code', 'name', 'category', 'feePerBooking', 'coveredByFeatureCode'] as const

/** New type when `type` is omitted, otherwise Edit. Codes are immutable, so Edit locks the Code field. */
export function EquipmentTypeFormDialog({ type, onClose }: { type?: EquipmentTypeDto; onClose: () => void }) {
  const categories = useEquipmentCategories()
  const features = useFeatures()
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<TypeForm>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: type?.code ?? '',
      name: type?.name ?? '',
      category: type?.category ?? '',
      feePerBooking: type ? String(type.feePerBooking) : '',
      coveredByFeatureCode: type?.coveredByFeatureCode ?? '',
    },
  })
  const mutation = useApiMutation<TypeForm, EquipmentTypeDto, TypeForm>({
    mutationFn: async (values) => {
      const body: EquipmentTypeRequest = {
        // A disabled field is not submitted, so send the stored code.
        code: type ? type.code : values.code,
        name: values.name,
        category: values.category,
        feePerBooking: Number(values.feePerBooking.trim()),
        coveredByFeatureCode: values.coveredByFeatureCode || null,
      }
      return type
        ? (await api.put<EquipmentTypeDto>(`/api/equipment-types/${type.id}`, body)).data
        : (await api.post<EquipmentTypeDto>('/api/equipment-types', body)).data
    },
    invalidate: [equipmentTypesKeys.all, equipmentItemsKeys.all],
    successMessage: type ? 'Equipment type updated' : 'Equipment type created',
    form: { setError, fields: FIELDS },
    conflictField: 'code',
    onSuccess: onClose,
  })

  // Keep a stored category or feature listed while its list is still loading.
  const categoryOptions = categories.data ?? (type ? [type.category] : [])
  const featureOptions =
    features.data ??
    (type?.coveredByFeatureCode ? [{ code: type.coveredByFeatureCode, name: type.coveredByFeatureName ?? '' }] : [])

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="equipment-type-form-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="equipment-type-form-title">{type ? `Edit ${type.code}` : 'New equipment type'}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <Controller
              name="code"
              control={control}
              render={({ field }) => (
                <TextField
                  label="Code"
                  {...field}
                  onChange={(e) => field.onChange(e.target.value.toUpperCase())}
                  disabled={!!type}
                  error={!!errors.code}
                  helperText={errors.code?.message ?? (type ? TYPE_CODE_LOCKED : TYPE_CODE_HINT)}
                />
              )}
            />
            <TextField label="Name" {...register('name')} error={!!errors.name} helperText={errors.name?.message} />
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <Controller
                name="category"
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors.category} sx={{ flex: 1 }}>
                    <InputLabel id="equipment-type-category-label">Category</InputLabel>
                    <Select labelId="equipment-type-category-label" label="Category" {...field}>
                      {categoryOptions.map((c) => (
                        <MenuItem key={c} value={c}>
                          {c}
                        </MenuItem>
                      ))}
                    </Select>
                    {errors.category && <FormHelperText>{errors.category.message}</FormHelperText>}
                  </FormControl>
                )}
              />
              <TextField
                label="Fee per booking (LKR)"
                {...register('feePerBooking')}
                error={!!errors.feePerBooking}
                helperText={errors.feePerBooking?.message}
                slotProps={{ htmlInput: { inputMode: 'decimal' } }}
                sx={{ flex: 1 }}
              />
            </Stack>
            <Controller
              name="coveredByFeatureCode"
              control={control}
              render={({ field }) => (
                <FormControl error={!!errors.coveredByFeatureCode}>
                  <InputLabel id="equipment-type-covered-label">Covered by room feature</InputLabel>
                  <Select labelId="equipment-type-covered-label" label="Covered by room feature" {...field}>
                    <MenuItem value="">None</MenuItem>
                    {featureOptions.map((f) => (
                      <MenuItem key={f.code} value={f.code}>
                        {f.name} ({f.code})
                      </MenuItem>
                    ))}
                  </Select>
                  <FormHelperText>{errors.coveredByFeatureCode?.message ?? COVERED_BY_HINT}</FormHelperText>
                </FormControl>
              )}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            {type ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
