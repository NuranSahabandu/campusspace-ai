import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Autocomplete,
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
import type { BuildingRefDto, RoomDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { ROOM_TYPES, roomTypeLabel } from './roomTypes'
import { roomsKeys, useBuildings, useFeatures } from './useFacilities'

// Mirrors CreateRoomRequest / UpdateRoomRequest; the server remains the real validator (§12)
// and checks what annotations cannot: the building is active and every feature code exists.
const schema = z.object({
  code: z.string().trim().min(1, 'Code is required').max(20, 'At most 20 characters'),
  name: z.string().trim().min(1, 'Name is required').max(100, 'At most 100 characters'),
  type: z.string().refine((v) => (ROOM_TYPES as readonly string[]).includes(v), 'Choose a type'),
  capacity: z.number({ error: 'Capacity is required' }).int('Whole number only').min(1, 'At least 1'),
  // A Select value is a string; '' means nothing chosen.
  buildingId: z.string().min(1, 'Choose a building'),
  featureCodes: z.array(z.string()),
  isActive: z.boolean(),
})

type RoomForm = z.infer<typeof schema>

const FIELDS = ['code', 'name', 'type', 'capacity', 'buildingId', 'featureCodes', 'isActive'] as const

/**
 * New room when `room` is omitted, otherwise Edit. Edit adds the Active switch, which is how a
 * deactivated room is reactivated. Saving replaces the room's whole feature set.
 */
export function RoomFormDialog({ room, onClose }: { room?: RoomDto; onClose: () => void }) {
  const buildings = useBuildings()
  const features = useFeatures()
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<RoomForm>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: room?.code ?? '',
      name: room?.name ?? '',
      type: room?.type ?? '',
      capacity: room?.capacity,
      buildingId: room ? String(room.building.id) : '',
      featureCodes: room?.features.map((f) => f.code) ?? [],
      isActive: room?.isActive ?? true,
    },
  })
  const mutation = useApiMutation<RoomForm, RoomDto, RoomForm>({
    mutationFn: async ({ isActive, buildingId, ...rest }) => {
      const body = { ...rest, buildingId: Number(buildingId) }
      return room
        ? (await api.put<RoomDto>(`/api/rooms/${room.id}`, { ...body, isActive })).data
        : (await api.post<RoomDto>('/api/rooms', body)).data
    },
    invalidate: [roomsKeys.all],
    successMessage: room ? 'Room updated' : 'Room created',
    form: { setError, fields: FIELDS },
    conflictField: 'code',
    onSuccess: onClose,
  })

  // New rooms need an active building; keep the current one listed when editing, even if it is inactive.
  const buildingOptions: BuildingRefDto[] = (buildings.data ?? []).filter((b) => b.isActive)
  if (room && !buildingOptions.some((b) => b.id === room.building.id)) buildingOptions.push(room.building)
  const featureCodes = (features.data ?? []).map((f) => f.code)
  const featureName = (code: string) => features.data?.find((f) => f.code === code)?.name ?? code

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="room-form-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="room-form-title">{room ? `Edit ${room.code}` : 'New room'}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField label="Code" {...register('code')} error={!!errors.code} helperText={errors.code?.message} />
              <TextField
                label="Name"
                {...register('name')}
                error={!!errors.name}
                helperText={errors.name?.message}
                sx={{ flex: 1 }}
              />
            </Stack>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <Controller
                name="buildingId"
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors.buildingId} sx={{ flex: 1 }}>
                    <InputLabel id="room-building-label">Building</InputLabel>
                    <Select labelId="room-building-label" label="Building" {...field}>
                      {buildingOptions.map((b) => (
                        <MenuItem key={b.id} value={String(b.id)}>
                          {b.code} · {b.name}
                        </MenuItem>
                      ))}
                    </Select>
                    {errors.buildingId && <FormHelperText>{errors.buildingId.message}</FormHelperText>}
                  </FormControl>
                )}
              />
              <Controller
                name="type"
                control={control}
                render={({ field }) => (
                  <FormControl error={!!errors.type} sx={{ flex: 1 }}>
                    <InputLabel id="room-type-label">Type</InputLabel>
                    <Select labelId="room-type-label" label="Type" {...field}>
                      {ROOM_TYPES.map((t) => (
                        <MenuItem key={t} value={t}>
                          {roomTypeLabel(t)}
                        </MenuItem>
                      ))}
                    </Select>
                    {errors.type && <FormHelperText>{errors.type.message}</FormHelperText>}
                  </FormControl>
                )}
              />
              <TextField
                label="Capacity"
                type="number"
                {...register('capacity', { valueAsNumber: true })}
                error={!!errors.capacity}
                helperText={errors.capacity?.message}
                slotProps={{ htmlInput: { min: 1 } }}
                sx={{ width: { sm: 130 } }}
              />
            </Stack>
            <Controller
              name="featureCodes"
              control={control}
              render={({ field }) => (
                <Autocomplete
                  multiple
                  options={featureCodes}
                  value={field.value}
                  onChange={(_, value) => field.onChange(value)}
                  onBlur={field.onBlur}
                  getOptionLabel={(code) => `${code} (${featureName(code)})`}
                  renderInput={(params) => (
                    <TextField
                      {...params}
                      label="Features"
                      error={!!errors.featureCodes}
                      helperText={errors.featureCodes?.message}
                    />
                  )}
                />
              )}
            />
            {room && (
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
            {room ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
