import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  FormHelperText,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  Switch,
  TextField,
} from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import type { PricingRuleDto, PricingRuleRequest } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { campusToday, formatDateOnly } from '../../ui/formatDateTime'
import { MAX_RATE, REQUESTER_ROLES, ROOM_TYPES, roomTypeLabel } from './pricingValues'
import { pricingKeys } from './usePricing'

export const PAST_DATE_MESSAGE = "Start date can't be in the past."

// Mirrors PricingRuleRequest and PricingRuleService.ApplyAsync (same messages); the server remains the real validator (§12).
const schema = z
  .object({
    roomType: z.string().min(1, 'Choose a room type'),
    requesterRole: z.string().min(1, 'Choose a requester role'),
    isExempt: z.boolean(),
    // Kept as the typed text so "12.345" can be rejected rather than rounded.
    hourlyRate: z.string(),
    // "yyyy-MM-dd" from the date input; campus dates compare correctly as text.
    validFrom: z
      .string()
      .min(1, 'Start date is required')
      .refine((date) => date >= campusToday(), PAST_DATE_MESSAGE),
  })
  .superRefine(({ isExempt, hourlyRate }, ctx) => {
    if (isExempt) return
    const value = hourlyRate.trim()
    const fail = (message: string) => ctx.addIssue({ code: 'custom', path: ['hourlyRate'], message })
    if (value === '') return fail('Rate is required')
    const rate = Number(value)
    if (!Number.isFinite(rate)) return fail('Enter a number')
    if (rate < 0) return fail("Rate can't be negative")
    if (!/^\d+(\.\d{1,2})?$/.test(value)) return fail('Rate can have at most 2 decimal places.')
    if (rate > MAX_RATE) return fail('At most 99,999,999.99')
  })

type RuleForm = z.infer<typeof schema>

const FIELDS = ['roomType', 'requesterRole', 'isExempt', 'hourlyRate', 'validFrom'] as const

/** New rule when `rule` is omitted, otherwise Edit. Only Scheduled rules are offered for editing. */
export function PricingRuleFormDialog({ rule, onClose }: { rule?: PricingRuleDto; onClose: () => void }) {
  const today = campusToday()
  const { register, control, handleSubmit, setError, setValue, clearErrors, formState: { errors } } = useForm<RuleForm>({
    resolver: zodResolver(schema),
    defaultValues: {
      roomType: rule?.roomType ?? '',
      requesterRole: rule?.requesterRole ?? '',
      isExempt: rule?.isExempt ?? false,
      hourlyRate: rule ? String(rule.hourlyRate) : '',
      validFrom: rule?.validFrom ?? '',
    },
  })
  const isExempt = useWatch({ control, name: 'isExempt' })

  const mutation = useApiMutation<RuleForm, PricingRuleDto, RuleForm>({
    mutationFn: async (values) => {
      const body: PricingRuleRequest = {
        roomType: values.roomType,
        requesterRole: values.requesterRole,
        hourlyRate: values.isExempt ? 0 : Number(values.hourlyRate.trim()),
        isExempt: values.isExempt,
        validFrom: values.validFrom,
      }
      return rule
        ? (await api.put<PricingRuleDto>(`/api/pricing-rules/${rule.id}`, body)).data
        : (await api.post<PricingRuleDto>('/api/pricing-rules', body)).data
    },
    invalidate: [pricingKeys.all],
    successMessage: rule ? 'Rule updated' : 'Rule created',
    form: { setError, fields: FIELDS },
    // The only 409 is a duplicate room type, role and start date.
    conflictField: 'validFrom',
    onSuccess: onClose,
  })

  const title = rule
    ? `Edit ${roomTypeLabel(rule.roomType)} / ${rule.requesterRole} from ${formatDateOnly(rule.validFrom)}`
    : 'New pricing rule'

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="pricing-rule-form-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="pricing-rule-form-title">{title}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <Controller
                name="roomType"
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors.roomType} sx={{ flex: 1 }}>
                    <InputLabel id="pricing-rule-room-type-label">Room type</InputLabel>
                    <Select labelId="pricing-rule-room-type-label" label="Room type" {...field}>
                      {ROOM_TYPES.map((t) => (
                        <MenuItem key={t} value={t}>
                          {roomTypeLabel(t)}
                        </MenuItem>
                      ))}
                    </Select>
                    {errors.roomType && <FormHelperText>{errors.roomType.message}</FormHelperText>}
                  </FormControl>
                )}
              />
              <Controller
                name="requesterRole"
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors.requesterRole} sx={{ flex: 1 }}>
                    <InputLabel id="pricing-rule-role-label">Requester role</InputLabel>
                    <Select labelId="pricing-rule-role-label" label="Requester role" {...field}>
                      {REQUESTER_ROLES.map((r) => (
                        <MenuItem key={r} value={r}>
                          {r}
                        </MenuItem>
                      ))}
                    </Select>
                    {errors.requesterRole && <FormHelperText>{errors.requesterRole.message}</FormHelperText>}
                  </FormControl>
                )}
              />
            </Stack>
            <Controller
              name="isExempt"
              control={control}
              render={({ field }) => (
                <FormControl error={!!errors.isExempt}>
                  <FormControlLabel
                    label="Exempt (free)"
                    control={
                      <Switch
                        checked={field.value}
                        onChange={(e) => {
                          field.onChange(e.target.checked)
                          // An exempt rule must have a rate of 0 (server rule); turning it off asks for a rate again.
                          setValue('hourlyRate', e.target.checked ? '0' : '')
                          clearErrors('hourlyRate')
                        }}
                      />
                    }
                  />
                  {errors.isExempt && <FormHelperText>{errors.isExempt.message}</FormHelperText>}
                </FormControl>
              )}
            />
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField
                label="Hourly rate (LKR)"
                {...register('hourlyRate')}
                disabled={isExempt}
                error={!!errors.hourlyRate}
                helperText={errors.hourlyRate?.message ?? (isExempt ? 'Exempt rules are free' : undefined)}
                slotProps={{ htmlInput: { inputMode: 'decimal' } }}
                sx={{ flex: 1 }}
              />
              <TextField
                label="Valid from"
                type="date"
                {...register('validFrom')}
                error={!!errors.validFrom}
                helperText={errors.validFrom?.message ?? 'Campus date; today or later'}
                slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: today } }}
                sx={{ flex: 1 }}
              />
            </Stack>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            {rule ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
