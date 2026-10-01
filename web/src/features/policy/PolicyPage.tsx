import { useMemo, useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Box,
  Button,
  FormControl,
  FormHelperText,
  InputAdornment,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Skeleton,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { api } from '../../api/client'
import type { PolicySettingDto, PolicySettingsUpdateRequest } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { formatDateTime } from '../../ui/formatDateTime'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { OpeningHoursEditor } from './OpeningHoursEditor'
import {
  changedKeys,
  describeChanges,
  GRANULARITIES,
  NUMBER_FIELDS,
  POLICY_FIELDS,
  type PolicyForm,
  type PolicyKey,
  policySchema,
  serialize,
  toFormValues,
} from './policyForm'
import { policyKeys, usePolicySettings } from './usePolicy'

export const APPLIES_IMMEDIATELY = 'Changes apply immediately to new checks, including requests awaiting approval.'

/** "Last updated … by …" from the most recent change by a person, or "Default values" if nobody has changed any. */
function lastUpdated(settings: readonly PolicySettingDto[]) {
  const latest = settings
    .filter((s) => s.updatedByName)
    .reduce<PolicySettingDto | null>((a, s) => (!a || s.updatedAt > a.updatedAt ? s : a), null)
  return latest ? `Last updated ${formatDateTime(latest.updatedAt)} by ${latest.updatedByName}` : 'Default values'
}

function PolicyEditor({ settings }: { settings: PolicySettingDto[] }) {
  // The values the form started from; replaced by the server's response after a save. Background refetches don't
  // touch the form, so they never wipe an unsaved edit.
  const [baseline, setBaseline] = useState(() => toFormValues(settings))
  const baselineText = useMemo(() => serialize(baseline), [baseline])
  const descriptions = Object.fromEntries(settings.map((s) => [s.key, s.description]))

  const { register, control, handleSubmit, reset, setError, setValue, formState: { errors } } = useForm<PolicyForm>({
    resolver: zodResolver(policySchema),
    defaultValues: baseline,
  })
  const values = useWatch({ control }) as PolicyForm
  const changed = changedKeys(baselineText, values)
  const [confirming, setConfirming] = useState<{ form: PolicyForm; keys: PolicyKey[] } | null>(null)

  const mutation = useApiMutation<PolicySettingsUpdateRequest, PolicySettingDto[], PolicyForm>({
    mutationFn: async (body) => (await api.put<PolicySettingDto[]>('/api/policy-settings', body)).data,
    invalidate: [policyKeys.all],
    successMessage: 'Booking policy saved',
    form: { setError, fields: POLICY_FIELDS },
    onSuccess: (saved) => {
      const next = toFormValues(saved)
      setBaseline(next)
      reset(next)
    },
  })

  const save = () => {
    if (!confirming) return
    const text = serialize(confirming.form)
    // Only the changed keys: the server re-checks the whole policy with them merged in.
    mutation.mutate(
      { settings: confirming.keys.map((key) => ({ key, value: text[key] })) },
      { onSettled: () => setConfirming(null) },
    )
  }

  return (
    <form noValidate onSubmit={handleSubmit((form) => setConfirming({ form, keys: changedKeys(baselineText, form) }))}>
      {errors.root?.server && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errors.root.server.message}
        </Alert>
      )}
      <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
        <Typography variant="h6" component="h2">
          Opening hours
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {descriptions.opening_hours}
        </Typography>
        <OpeningHoursEditor control={control} register={register} setValue={setValue} errors={errors} />
      </Paper>

      <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
        <Typography variant="h6" component="h2" sx={{ mb: 2 }}>
          Limits
        </Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
          {NUMBER_FIELDS.map(({ key, label, unit }) =>
            key === 'slot_granularity_minutes' ? (
              <Controller
                key={key}
                name={key}
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors[key]}>
                    <InputLabel id="slot-granularity-label">{label}</InputLabel>
                    <Select labelId="slot-granularity-label" label={label} {...field}>
                      {GRANULARITIES.map((g) => (
                        <MenuItem key={g} value={g}>
                          {g} {unit}
                        </MenuItem>
                      ))}
                    </Select>
                    <FormHelperText>{errors[key]?.message ?? descriptions[key]}</FormHelperText>
                  </FormControl>
                )}
              />
            ) : (
              <TextField
                key={key}
                label={label}
                {...register(key)}
                error={!!errors[key]}
                helperText={errors[key]?.message ?? descriptions[key]}
                slotProps={{
                  htmlInput: { inputMode: key === 'max_capacity_ratio' ? 'decimal' : 'numeric' },
                  input: { endAdornment: <InputAdornment position="end">{unit}</InputAdornment> },
                }}
              />
            ),
          )}
        </Box>
      </Paper>

      <Stack direction="row" spacing={2} sx={{ justifyContent: 'flex-end' }}>
        <Button onClick={() => reset(baseline)} disabled={changed.length === 0 || mutation.isPending}>
          Reset
        </Button>
        <Button type="submit" variant="contained" disabled={changed.length === 0 || mutation.isPending}>
          Save
        </Button>
      </Stack>

      <ConfirmDialog
        open={confirming !== null}
        title="Save booking policy?"
        message={APPLIES_IMMEDIATELY}
        confirmLabel="Save"
        pending={mutation.isPending}
        onConfirm={save}
        onClose={() => setConfirming(null)}
      >
        <Box component="ul" aria-label="Changes" sx={{ mb: 0 }}>
          {confirming &&
            describeChanges(baseline, confirming.form, confirming.keys).map((line) => <li key={line}>{line}</li>)}
        </Box>
      </ConfirmDialog>
    </form>
  )
}

/** Facilities Officer booking policy (addendum A.1–A.2, UC29): the PolicySettings every booking check reads. */
export function PolicyPage() {
  const { data, isPending, isError, error, refetch } = usePolicySettings()

  let content
  if (isError) {
    content = (
      <QueryErrorAlert error={error} what="the booking policy" onRetry={() => refetch()} />
    )
  } else if (isPending) {
    content = <Skeleton variant="rectangular" height={400} aria-label="Loading booking policy" />
  } else if (data.length === 0) {
    content = <Alert severity="warning">No booking policy settings found. Run the database migrations and seed.</Alert>
  } else {
    content = (
      <>
        <Typography color="text.secondary" sx={{ mb: 2 }}>
          {lastUpdated(data)}
        </Typography>
        <PolicyEditor settings={data} />
      </>
    )
  }

  return (
    <>
      <Typography variant="h4" component="h1" sx={{ mb: 1 }}>
        Booking policy
      </Typography>
      {content}
    </>
  )
}
