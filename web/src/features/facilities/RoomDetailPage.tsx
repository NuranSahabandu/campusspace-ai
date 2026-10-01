import { useState } from 'react'
import { Link as RouterLink, useParams } from 'react-router'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import EditIcon from '@mui/icons-material/Edit'
import { Alert, Button, Chip, LinearProgress, Paper, Stack, Typography } from '@mui/material'
import { parseProblem } from '../../api/problem'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { BlackoutsSection } from './BlackoutsSection'
import { FeatureChips } from './FeatureChips'
import { RoomFormDialog } from './RoomFormDialog'
import { roomTypeLabel } from './roomTypes'
import { useRoom } from './useFacilities'

/** One room (§12): its details, an Edit dialog and its maintenance blackouts. */
export function RoomDetailPage() {
  const id = Number(useParams().id)
  const { data: room, isPending, isError, error, refetch } = useRoom(id)
  const [editing, setEditing] = useState(false)

  const back = (
    <Button component={RouterLink} to="/rooms" startIcon={<ArrowBackIcon />} sx={{ mb: 1 }}>
      Rooms
    </Button>
  )

  // A non-numeric id (/rooms/abc) never reaches the API (the query is disabled); it is not found, like a 404.
  const validId = Number.isInteger(id) && id > 0
  if (!validId || isError) {
    return (
      <>
        {back}
        {!validId || parseProblem(error).status === 404 ? (
          <Alert severity="warning">Room not found</Alert>
        ) : (
          <QueryErrorAlert error={error} what="the room" onRetry={() => refetch()} />
        )}
      </>
    )
  }
  if (isPending) return <LinearProgress aria-label="Loading room" />

  const facts = [
    ['Building', `${room.building.code} · ${room.building.name}`],
    ['Type', roomTypeLabel(room.type)],
    ['Capacity', String(room.capacity)],
  ]

  return (
    <>
      {back}
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Typography variant="h4" component="h1">
            {room.code} · {room.name}
          </Typography>
          <Chip label={room.isActive ? 'Active' : 'Inactive'} color={room.isActive ? 'success' : 'default'} />
        </Stack>
        <Button variant="outlined" startIcon={<EditIcon />} onClick={() => setEditing(true)}>
          Edit
        </Button>
      </Stack>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={4}>
          {facts.map(([label, value]) => (
            <div key={label}>
              <Typography variant="caption" color="text.secondary">
                {label}
              </Typography>
              <Typography>{value}</Typography>
            </div>
          ))}
          <div>
            <Typography variant="caption" color="text.secondary">
              Features
            </Typography>
            {room.features.length ? (
              <FeatureChips features={room.features} />
            ) : (
              <Typography color="text.secondary">None</Typography>
            )}
          </div>
        </Stack>
      </Paper>

      <BlackoutsSection room={room} />

      {editing && <RoomFormDialog room={room} onClose={() => setEditing(false)} />}
    </>
  )
}
