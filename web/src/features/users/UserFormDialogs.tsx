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
import type { CreateUserRequest, UpdateUserRequest, UserDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { ALL_ROLES } from '../../auth/roles'
import { usersKeys } from './useUsers'

// Mirror CreateUserRequest / UpdateUserRequest annotations; the server remains the real validator (§12).
const fullName = z.string().trim().min(1, 'Full name is required').max(100, 'At most 100 characters')
const role = z.string().refine((r) => (ALL_ROLES as readonly string[]).includes(r), 'Choose a role')

const createSchema = z.object({
  fullName,
  email: z.email('Enter a valid email address').max(256, 'At most 256 characters'),
  password: z.string().min(8, 'At least 8 characters').max(100, 'At most 100 characters'),
  role,
})

const editSchema = z.object({ fullName, role, isActive: z.boolean() })

type CreateForm = z.infer<typeof createSchema>
type EditForm = z.infer<typeof editSchema>

export const OWN_ACCOUNT_HINT = 'You cannot change your own role or deactivate yourself.'

function RoleSelect({ value, onChange, error, disabled, helperText }: {
  value: string
  onChange: (value: string) => void
  error?: string
  disabled?: boolean
  helperText?: string
}) {
  return (
    <FormControl error={!!error} disabled={disabled} fullWidth>
      <InputLabel id="user-role-label">Role</InputLabel>
      <Select labelId="user-role-label" label="Role" value={value} onChange={(e) => onChange(e.target.value)}>
        {ALL_ROLES.map((r) => (
          <MenuItem key={r} value={r}>
            {r}
          </MenuItem>
        ))}
      </Select>
      {(error ?? helperText) && <FormHelperText>{error ?? helperText}</FormHelperText>}
    </FormControl>
  )
}

export function CreateUserDialog({ onClose }: { onClose: () => void }) {
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<CreateForm>({
    resolver: zodResolver(createSchema),
    defaultValues: { fullName: '', email: '', password: '', role: '' },
  })
  const mutation = useApiMutation<CreateUserRequest, UserDto, CreateForm>({
    mutationFn: async (body) => (await api.post<UserDto>('/api/users', body)).data,
    invalidate: [usersKeys.all],
    successMessage: 'User created',
    form: { setError, fields: ['fullName', 'email', 'password', 'role'] },
    conflictField: 'email',
    onSuccess: onClose,
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="create-user-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="create-user-title">New user</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <TextField label="Full name" {...register('fullName')} error={!!errors.fullName} helperText={errors.fullName?.message} />
            <TextField label="Email" type="email" {...register('email')} error={!!errors.email} helperText={errors.email?.message} />
            <TextField
              label="Password"
              type="password"
              autoComplete="new-password"
              {...register('password')}
              error={!!errors.password}
              helperText={errors.password?.message}
            />
            <Controller
              name="role"
              control={control}
              render={({ field }) => (
                <RoleSelect value={field.value} onChange={field.onChange} error={errors.role?.message} />
              )}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            Create
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}

/** isSelf disables role and active: the API rejects an Admin demoting or deactivating themselves. */
export function EditUserDialog({ user, isSelf, onClose }: { user: UserDto; isSelf: boolean; onClose: () => void }) {
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<EditForm>({
    resolver: zodResolver(editSchema),
    defaultValues: { fullName: user.fullName, role: user.role, isActive: user.isActive },
  })
  const mutation = useApiMutation<UpdateUserRequest, UserDto, EditForm>({
    mutationFn: async (body) => (await api.put<UserDto>(`/api/users/${user.id}`, body)).data,
    invalidate: [usersKeys.all],
    successMessage: 'User updated',
    form: { setError, fields: ['fullName', 'role', 'isActive'] },
    onSuccess: onClose,
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="edit-user-title">
      <form noValidate onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <DialogTitle id="edit-user-title">Edit {user.email}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <TextField label="Full name" {...register('fullName')} error={!!errors.fullName} helperText={errors.fullName?.message} />
            <Controller
              name="role"
              control={control}
              render={({ field }) => (
                <RoleSelect
                  value={field.value}
                  onChange={field.onChange}
                  error={errors.role?.message}
                  disabled={isSelf}
                  helperText={isSelf ? OWN_ACCOUNT_HINT : undefined}
                />
              )}
            />
            <FormControl error={!!errors.isActive}>
              <Controller
                name="isActive"
                control={control}
                render={({ field }) => (
                  <FormControlLabel
                    label="Active"
                    disabled={isSelf}
                    control={<Switch checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                  />
                )}
              />
              {errors.isActive && <FormHelperText>{errors.isActive.message}</FormHelperText>}
            </FormControl>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            Save
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
