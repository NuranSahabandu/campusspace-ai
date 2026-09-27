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
import type { EquipmentItemDto, EquipmentItemRequest, EquipmentTypeRefDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import {
  DAMAGED_MESSAGE,
  EDITABLE_STATUSES,
  EQUIPMENT_CONDITIONS,
  equipmentValueLabel,
} from './equipmentValues'
import { equipmentItemsKeys, equipmentTypesKeys, useEquipmentTypeOptions } from './useEquipment'

export const TYPE_LOCKED = "An item's type can't be changed"
export const ON_LOAN_NOTICE = "This item is on loan. Only notes can be edited until it's checked in."

// Mirrors EquipmentItemRequest and the checks in EquipmentItemService; the server remains the real validator (§12).
const schema = z
  .object({
    assetTag: z.string().trim().toUpperCase().min(1, 'Asset tag is required').max(30, 'At most 30 characters'),
    // A Select value is a string; '' means nothing chosen.
    typeId: z.string().min(1, 'Choose a type'),
    condition: z.string().min(1, 'Choose a condition'),
    status: z.string().min(1, 'Choose a status'),
    notes: z.string().max(500, 'At most 500 characters'),
  })
  .superRefine((values, ctx) => {
    // An item on loan keeps its stored status and condition; check-in (Phase 2) decides them.
    if (values.status === 'OnLoan') return
    if (values.condition === 'Damaged' && values.status !== 'UnderRepair' && values.status !== 'Retired')
      ctx.addIssue({ code: 'custom', path: ['condition'], message: DAMAGED_MESSAGE })
  })

type ItemForm = z.infer<typeof schema>

const FIELDS = ['assetTag', 'typeId', 'condition', 'status', 'notes'] as const

/**
 * New item when `item` is omitted, otherwise Edit. The type is fixed after creation. An item on loan accepts only a
 * Notes change, so its other fields are locked and their stored values are sent.
 */
export function EquipmentItemFormDialog({ item, onClose }: { item?: EquipmentItemDto; onClose: () => void }) {
  const types = useEquipmentTypeOptions()
  const onLoan = item?.status === 'OnLoan'
  const { control, register, handleSubmit, setError, formState: { errors } } = useForm<ItemForm>({
    resolver: zodResolver(schema),
    defaultValues: {
      assetTag: item?.assetTag ?? '',
      typeId: item ? String(item.typeId) : '',
      condition: item?.condition ?? 'Good',
      status: item?.status ?? 'Available',
      notes: item?.notes ?? '',
    },
  })
  const mutation = useApiMutation<ItemForm, EquipmentItemDto, ItemForm>({
    mutationFn: async (values) => {
      const notes = values.notes.trim() || null
      // Disabled fields are not submitted, so send the stored values for them.
      const body: EquipmentItemRequest = item
        ? onLoan
          ? { assetTag: item.assetTag, typeId: item.typeId, condition: item.condition, status: item.status, notes }
          : { ...values, typeId: item.typeId, notes }
        : { ...values, typeId: Number(values.typeId), notes }
      return item
        ? (await api.put<EquipmentItemDto>(`/api/equipment-items/${item.id}`, body)).data
        : (await api.post<EquipmentItemDto>('/api/equipment-items', body)).data
    },
    invalidate: [equipmentItemsKeys.all, equipmentTypesKeys.all],
    successMessage: item ? 'Item updated' : 'Item created',
    form: { setError, fields: FIELDS },
    conflictField: 'assetTag',
    onSuccess: onClose,
  })

  // Keep the item's own type listed even if it is outside the loaded options.
  const typeOptions: EquipmentTypeRefDto[] = [...(types.data ?? [])]
  if (item && !typeOptions.some((t) => t.id === item.typeId))
    typeOptions.push({ id: item.typeId, code: item.typeCode, name: item.typeName })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="equipment-item-form-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="equipment-item-form-title">{item ? `Edit ${item.assetTag}` : 'New equipment item'}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {onLoan && <Alert severity="info">{ON_LOAN_NOTICE}</Alert>}
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <Controller
                name="assetTag"
                control={control}
                render={({ field }) => (
                  <TextField
                    label="Asset tag"
                    {...field}
                    onChange={(e) => field.onChange(e.target.value.toUpperCase())}
                    disabled={onLoan}
                    error={!!errors.assetTag}
                    helperText={errors.assetTag?.message}
                    sx={{ flex: 1 }}
                  />
                )}
              />
              <Controller
                name="typeId"
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors.typeId} disabled={!!item} sx={{ flex: 1.5 }}>
                    <InputLabel id="equipment-item-type-label">Type</InputLabel>
                    <Select labelId="equipment-item-type-label" label="Type" {...field}>
                      {typeOptions.map((t) => (
                        <MenuItem key={t.id} value={String(t.id)}>
                          {t.code} — {t.name}
                        </MenuItem>
                      ))}
                    </Select>
                    {(errors.typeId || item) && <FormHelperText>{errors.typeId?.message ?? TYPE_LOCKED}</FormHelperText>}
                  </FormControl>
                )}
              />
            </Stack>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <Controller
                name="condition"
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors.condition} disabled={onLoan} sx={{ flex: 1 }}>
                    <InputLabel id="equipment-item-condition-label">Condition</InputLabel>
                    <Select labelId="equipment-item-condition-label" label="Condition" {...field}>
                      {EQUIPMENT_CONDITIONS.map((c) => (
                        <MenuItem key={c} value={c}>
                          {equipmentValueLabel(c)}
                        </MenuItem>
                      ))}
                    </Select>
                    {errors.condition && <FormHelperText>{errors.condition.message}</FormHelperText>}
                  </FormControl>
                )}
              />
              <Controller
                name="status"
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors.status} disabled={onLoan} sx={{ flex: 1 }}>
                    <InputLabel id="equipment-item-status-label">Status</InputLabel>
                    <Select labelId="equipment-item-status-label" label="Status" {...field}>
                      {EDITABLE_STATUSES.map((s) => (
                        <MenuItem key={s} value={s}>
                          {equipmentValueLabel(s)}
                        </MenuItem>
                      ))}
                      {/* Only so a stored OnLoan renders; it is never offered (loans set it). */}
                      {onLoan && (
                        <MenuItem value="OnLoan" disabled>
                          {equipmentValueLabel('OnLoan')}
                        </MenuItem>
                      )}
                    </Select>
                    {errors.status && <FormHelperText>{errors.status.message}</FormHelperText>}
                  </FormControl>
                )}
              />
            </Stack>
            <TextField
              label="Notes"
              {...register('notes')}
              multiline
              minRows={2}
              error={!!errors.notes}
              helperText={errors.notes?.message}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            {item ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
