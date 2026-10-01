import { useState } from 'react'
import { Link as RouterLink, useParams } from 'react-router'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import PersonAddIcon from '@mui/icons-material/PersonAdd'
import {
  Alert,
  Box,
  Button,
  Chip,
  LinearProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import { api } from '../../api/client'
import { parseProblem } from '../../api/problem'
import type { ClubDetailDto, ClubMemberDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { formatDateTime } from '../../ui/formatDateTime'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { AddMemberDialog } from './AddMemberDialog'
import { clubsKeys, useClub } from './useClubs'

/** One club and its members (§12). The members come with the detail, so the table needs no server paging. */
export function ClubDetailPage() {
  const id = Number(useParams().id)
  const { data: club, isPending, isError, error, refetch } = useClub(id)
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState<ClubMemberDto | null>(null)

  const makeRepresentative = useApiMutation<number, ClubDetailDto>({
    mutationFn: async (userId) => (await api.put<ClubDetailDto>(`/api/clubs/${id}/representative`, { userId })).data,
    invalidate: [clubsKeys.all],
    successMessage: 'Representative changed',
  })
  const removeMember = useApiMutation<number>({
    mutationFn: (userId) => api.delete(`/api/clubs/${id}/members/${userId}`),
    invalidate: [clubsKeys.all],
    successMessage: 'Member removed',
    onSuccess: () => setRemoving(null),
  })

  const back = (
    <Button component={RouterLink} to="/clubs" startIcon={<ArrowBackIcon />} sx={{ mb: 1 }}>
      Clubs
    </Button>
  )

  // A non-numeric id (/clubs/abc) never reaches the API (the query is disabled); it is not found, like a 404.
  const validId = Number.isInteger(id) && id > 0
  if (!validId || isError) {
    return (
      <>
        {back}
        {!validId || parseProblem(error).status === 404 ? (
          <Alert severity="warning">Club not found</Alert>
        ) : (
          <QueryErrorAlert error={error} what="the club" onRetry={() => refetch()} />
        )}
      </>
    )
  }
  if (isPending) return <LinearProgress aria-label="Loading club" />

  return (
    <>
      {back}
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Typography variant="h4" component="h1">
            {club.name}
          </Typography>
          <Chip label={club.isActive ? 'Active' : 'Inactive'} color={club.isActive ? 'success' : 'default'} />
        </Stack>
        {/* The server rejects members for an inactive club; say so up front. */}
        <Tooltip title={club.isActive ? '' : 'Reactivate the club to add members'}>
          <span>
            <Button
              variant="contained"
              startIcon={<PersonAddIcon />}
              disabled={!club.isActive}
              onClick={() => setAdding(true)}
            >
              Add member
            </Button>
          </span>
        </Tooltip>
      </Stack>

      <TableContainer component={Paper} variant="outlined">
        <Table aria-label="Members">
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Role</TableCell>
              <TableCell>Joined</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {club.members.length === 0 && (
              <TableRow>
                <TableCell colSpan={4}>
                  <Typography color="text.secondary">No members yet</Typography>
                </TableCell>
              </TableRow>
            )}
            {club.members.map((m) => (
              <TableRow key={m.userId}>
                <TableCell>
                  <Box component="span" sx={{ mr: 1 }}>
                    {m.fullName}
                  </Box>
                  {m.isRepresentative && <Chip size="small" color="primary" label="Representative" />}
                </TableCell>
                <TableCell>{m.role}</TableCell>
                <TableCell>{formatDateTime(m.joinedAt)}</TableCell>
                <TableCell align="right">
                  <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                    {!m.isRepresentative && (
                      <Button
                        size="small"
                        aria-label={`Make ${m.fullName} representative`}
                        disabled={makeRepresentative.isPending}
                        onClick={() => makeRepresentative.mutate(m.userId)}
                      >
                        Make representative
                      </Button>
                    )}
                    <Button size="small" color="error" aria-label={`Remove ${m.fullName}`} onClick={() => setRemoving(m)}>
                      Remove
                    </Button>
                  </Stack>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      {adding && <AddMemberDialog club={club} onClose={() => setAdding(false)} />}
      <ConfirmDialog
        open={removing !== null}
        title="Remove member?"
        message={`Remove ${removing?.fullName ?? ''} from ${club.name}?`}
        confirmLabel="Remove"
        destructive
        pending={removeMember.isPending}
        onConfirm={() => removing && removeMember.mutate(removing.userId)}
        onClose={() => setRemoving(null)}
      />
    </>
  )
}
