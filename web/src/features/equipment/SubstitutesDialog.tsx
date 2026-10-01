import { useEffect } from 'react'
import { Controller, useForm } from 'react-hook-form'
import {
  Alert,
  Autocomplete,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  LinearProgress,
  Stack,
  TextField,
} from '@mui/material'
import { api } from '../../api/client'
import type { EquipmentTypeDto, EquipmentTypeRefDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { equipmentTypesKeys, useEquipmentTypeOptions, useSubstitutes } from './useEquipment'

interface SubstitutesForm {
  substituteTypeIds: number[]
}

/** Replaces the whole set of types that can stand in for `type` when it runs short. */
export function SubstitutesDialog({ type, onClose }: { type: EquipmentTypeDto; onClose: () => void }) {
  const substitutes = useSubstitutes(type.id)
  const options = useEquipmentTypeOptions()
  const { control, handleSubmit, reset, setError, formState: { errors } } = useForm<SubstitutesForm>({
    defaultValues: { substituteTypeIds: [] },
  })

  useEffect(() => {
    if (substitutes.data) reset({ substituteTypeIds: substitutes.data.map((s) => s.id) })
  }, [substitutes.data, reset])

  const mutation = useApiMutation<SubstitutesForm, EquipmentTypeRefDto[], SubstitutesForm>({
    mutationFn: async (values) =>
      (await api.put<EquipmentTypeRefDto[]>(`/api/equipment-types/${type.id}/substitutes`, values)).data,
    invalidate: [equipmentTypesKeys.all],
    successMessage: 'Substitutes saved',
    form: { setError, fields: ['substituteTypeIds'] },
    onSuccess: onClose,
  })

  // A type cannot substitute for itself. Keep saved substitutes listed even if they fall outside the loaded options.
  const choices: EquipmentTypeRefDto[] = (options.data ?? []).filter((t) => t.id !== type.id)
  for (const s of substitutes.data ?? []) if (!choices.some((c) => c.id === s.id)) choices.push(s)
  const byId = new Map(choices.map((c) => [c.id, c]))

  const loading = substitutes.isPending || options.isPending
  const failed = substitutes.isError ? substitutes : options.isError ? options : null

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="substitutes-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="substitutes-title">Substitutes of {type.code}</DialogTitle>
        <DialogContent>
          <Stack spacing={2}>
            <DialogContentText>Types that can replace {type.code} when it's short.</DialogContentText>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            {loading && !failed && <LinearProgress aria-label="Loading substitutes" />}
            {failed && (
              <QueryErrorAlert error={failed.error} what="substitutes" onRetry={() => failed.refetch()} />
            )}
            {!loading && !failed && (
              <Controller
                name="substituteTypeIds"
                control={control}
                render={({ field }) => (
                  <Autocomplete
                    multiple
                    options={choices.map((c) => c.id)}
                    value={field.value}
                    onChange={(_, value) => field.onChange(value)}
                    onBlur={field.onBlur}
                    getOptionLabel={(id) => {
                      const t = byId.get(id)
                      return t ? `${t.code} (${t.name})` : `Type #${id}`
                    }}
                    renderInput={(params) => (
                      <TextField
                        {...params}
                        label="Substitutes"
                        error={!!errors.substituteTypeIds}
                        helperText={errors.substituteTypeIds?.message}
                      />
                    )}
                  />
                )}
              />
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={loading || !!failed || mutation.isPending}>
            Save
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
