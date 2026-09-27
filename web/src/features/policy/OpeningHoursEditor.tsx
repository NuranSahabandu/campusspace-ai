import { Controller, type Control, type FieldErrors, type UseFormRegister, type UseFormSetValue, useWatch } from 'react-hook-form'
import { Alert, Box, Stack, Switch, TextField, Typography } from '@mui/material'
import { DAYS, DEFAULT_CLOSE, DEFAULT_OPEN, type PolicyForm } from './policyForm'

interface Props {
  control: Control<PolicyForm>
  register: UseFormRegister<PolicyForm>
  setValue: UseFormSetValue<PolicyForm>
  errors: FieldErrors<PolicyForm>
}

/**
 * One row per weekday: an Open switch and open/close times. The time pickers step by the slot granularity currently in
 * the form, so the browser offers only times the policy allows. Closed days are sent as null.
 */
export function OpeningHoursEditor({ control, register, setValue, errors }: Props) {
  const granularity = Number(useWatch({ control, name: 'slot_granularity_minutes' })) || 30
  const hours = useWatch({ control, name: 'opening_hours' })
  // Whole-editor errors: "At least one day must be open." or a server error keyed opening_hours.
  const editorError = errors.opening_hours?.message

  return (
    <Stack spacing={1.5}>
      {editorError && <Alert severity="error">{editorError}</Alert>}
      {DAYS.map(({ key, label }) => {
        const isOpen = hours[key].isOpen
        const dayErrors = errors.opening_hours?.[key]
        const timeProps = (field: 'open' | 'close', name: string) => ({
          type: 'time',
          size: 'small' as const,
          disabled: !isOpen,
          error: !!dayErrors?.[field],
          helperText: dayErrors?.[field]?.message,
          slotProps: {
            inputLabel: { shrink: true },
            htmlInput: { step: granularity * 60, 'aria-label': `${label} ${name}` },
          },
          sx: { width: { xs: '100%', sm: 170 } },
        })
        return (
          <Box
            key={key}
            sx={{
              display: 'grid',
              gridTemplateColumns: { xs: '1fr 1fr', sm: '130px auto 170px 170px' },
              gap: 1.5,
              alignItems: 'start',
            }}
          >
            <Typography sx={{ pt: 1, fontWeight: 500 }}>{label}</Typography>
            <Controller
              name={`opening_hours.${key}.isOpen`}
              control={control}
              render={({ field }) => (
                <Stack direction="row" sx={{ alignItems: 'center' }}>
                  <Switch
                    checked={field.value}
                    onChange={(e) => {
                      field.onChange(e.target.checked)
                      if (e.target.checked && !hours[key].open) {
                        setValue(`opening_hours.${key}.open`, DEFAULT_OPEN)
                        setValue(`opening_hours.${key}.close`, DEFAULT_CLOSE)
                      }
                    }}
                    slotProps={{ input: { 'aria-label': `${label} open` } }}
                  />
                  <Typography variant="body2" color="text.secondary">
                    {field.value ? 'Open' : 'Closed'}
                  </Typography>
                </Stack>
              )}
            />
            <TextField label="Opens" {...register(`opening_hours.${key}.open`)} {...timeProps('open', 'opens')} />
            <TextField label="Closes" {...register(`opening_hours.${key}.close`)} {...timeProps('close', 'closes')} />
          </Box>
        )
      })}
    </Stack>
  )
}
