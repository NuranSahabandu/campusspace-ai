import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Autocomplete,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Stack,
  TextField,
} from '@mui/material'
import { z } from 'zod'
import { api } from '../../api/client'
import type { AddClubMemberRequest, ClubDetailDto, UserDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { Roles } from '../../auth/roles'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'
import { useUsers } from '../users/useUsers'
import { clubsKeys } from './useClubs'

const schema = z.object({
  userId: z.number().nullable().refine((v) => v !== null, 'Choose a person'),
  isRepresentative: z.boolean(),
})

// The form holds null until someone is picked; a valid submit always has a userId.
type AddMemberForm = z.input<typeof schema>
type AddMemberValues = z.output<typeof schema>

const OPTIONS_PER_ROLE = 10

/**
 * Only students and lecturers can join a club (ClubService.MemberRoles). The users list filters one role at a time,
 * so this searches both roles in parallel and merges the results. The server still enforces the rule (400).
 */
function useMemberCandidates(search: string, exclude: ReadonlySet<number>) {
  const base = { search, page: 1, pageSize: OPTIONS_PER_ROLE }
  const students = useUsers({ ...base, role: Roles.Student })
  const lecturers = useUsers({ ...base, role: Roles.Lecturer })
  const options = [...(students.data?.items ?? []), ...(lecturers.data?.items ?? [])]
    .filter((u) => u.isActive && !exclude.has(u.id))
    .sort((a, b) => a.fullName.localeCompare(b.fullName))
  return { options, loading: students.isFetching || lecturers.isFetching }
}

export function AddMemberDialog({ club, onClose }: { club: ClubDetailDto; onClose: () => void }) {
  const [input, setInput] = useState('')
  const [selected, setSelected] = useState<UserDto | null>(null)
  const search = useDebouncedValue(input.trim(), 300)
  const { options, loading } = useMemberCandidates(search, new Set(club.members.map((m) => m.userId)))

  const { control, handleSubmit, setError, formState: { errors } } = useForm<AddMemberForm, unknown, AddMemberValues>({
    resolver: zodResolver(schema),
    defaultValues: { userId: null, isRepresentative: false },
  })
  const mutation = useApiMutation<AddClubMemberRequest, ClubDetailDto, AddMemberForm>({
    mutationFn: async (body) => (await api.post<ClubDetailDto>(`/api/clubs/${club.id}/members`, body)).data,
    invalidate: [clubsKeys.all],
    successMessage: 'Member added',
    // A clubId error (inactive club) matches no field, so it shows in the Alert.
    form: { setError, fields: ['userId', 'isRepresentative'] },
    conflictField: 'userId',
    onSuccess: onClose,
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="add-member-title">
      <form
        noValidate
        onSubmit={handleSubmit(({ userId, isRepresentative }) =>
          mutation.mutate({ userId, isRepresentative }),
        )}
      >
        <DialogTitle id="add-member-title">Add member to {club.name}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {errors.root?.server && <Alert severity="error">{errors.root.server.message}</Alert>}
            <Controller
              name="userId"
              control={control}
              render={({ field }) => (
                <Autocomplete
                  options={options}
                  loading={loading}
                  value={selected}
                  onChange={(_, user) => {
                    setSelected(user)
                    field.onChange(user?.id ?? null)
                  }}
                  inputValue={input}
                  onInputChange={(_, value) => setInput(value)}
                  // The server already filtered by search.
                  filterOptions={(x) => x}
                  getOptionLabel={(u) => `${u.fullName} (${u.role})`}
                  isOptionEqualToValue={(a, b) => a.id === b.id}
                  noOptionsText="No students or lecturers match"
                  renderInput={(params) => (
                    <TextField
                      {...params}
                      label="Student or lecturer"
                      error={!!errors.userId}
                      helperText={errors.userId?.message}
                    />
                  )}
                />
              )}
            />
            <Controller
              name="isRepresentative"
              control={control}
              render={({ field }) => (
                <FormControlLabel
                  label="Make representative"
                  control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                />
              )}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={mutation.isPending}>
            Add
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
