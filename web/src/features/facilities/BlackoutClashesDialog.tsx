import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, Typography } from '@mui/material'
import type { BlackoutDto } from '../../api/types'
import { formatDateTime } from '../../ui/formatDateTime'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { ClashList } from './ClashList'
import { useBlackoutClashes } from './useFacilities'

/** A blackout's current clashes (GET .../clashes), each cancellable. Reloads after a cancellation. */
export function BlackoutClashesDialog({ blackout, onClose }: { blackout: BlackoutDto; onClose: () => void }) {
  const { data: clashes, isPending, isError, error, refetch } = useBlackoutClashes(blackout.roomId, blackout.id)

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="blackout-clashes-title">
      <DialogTitle id="blackout-clashes-title">Clashing bookings</DialogTitle>
      <DialogContent>
        {/* The blackout reason is officer text: a plain React text child. */}
        <Typography color="text.secondary">
          {formatDateTime(blackout.start)} – {formatDateTime(blackout.end)} · {blackout.reason}
        </Typography>
        {isPending ? (
          <LinearProgress aria-label="Loading clashes" sx={{ mt: 2 }} />
        ) : isError ? (
          <QueryErrorAlert error={error} what="the clashes" onRetry={() => refetch()} sx={{ mt: 2 }} />
        ) : clashes.length === 0 ? (
          <Alert severity="success" sx={{ mt: 2 }}>
            No active bookings clash with this blackout.
          </Alert>
        ) : (
          <ClashList clashes={clashes} blackoutReason={blackout.reason} />
        )}
      </DialogContent>
      <DialogActions>
        <Button variant="contained" onClick={onClose}>
          Close
        </Button>
      </DialogActions>
    </Dialog>
  )
}
