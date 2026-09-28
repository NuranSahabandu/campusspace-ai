import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import { Button, Link, List, ListItem, ListItemText, Stack } from '@mui/material'
import type { BlackoutClashDto } from '../../api/types'
import { formatCampusTimeRange } from '../../ui/formatDateTime'
import { CancelRequestDialog } from '../requests/CancelRequestDialog'

/** The reason an officer starts from when a blackout forces a cancellation (editable in the dialog). */
const clashCancelReason = (blackoutReason: string) => `Room unavailable: ${blackoutReason}`

/**
 * Active bookings a blackout clashes with (UC14), each with "Cancel booking", which cancels its request with a
 * required reason. Used by the add-blackout warning and a blackout row's clash list. Names and emails are plain React
 * text children, never HTML.
 */
export function ClashList({ clashes, blackoutReason }: { clashes: BlackoutClashDto[]; blackoutReason: string }) {
  const [cancelling, setCancelling] = useState<BlackoutClashDto | null>(null)

  return (
    <>
      <List dense aria-label="Clashing bookings">
        {clashes.map((c) => (
          <ListItem
            key={c.bookingId}
            disableGutters
            secondaryAction={
              <Button
                size="small"
                color="error"
                onClick={() => setCancelling(c)}
                aria-label={`Cancel booking ${formatCampusTimeRange(c.start, c.end)} for ${c.requesterName}`}
              >
                Cancel booking
              </Button>
            }
            sx={{ pr: 18 }}
          >
            <ListItemText
              primary={`${formatCampusTimeRange(c.start, c.end)} · ${c.status}`}
              secondary={
                <Stack component="span" direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
                  <span>{`${c.requesterName} (${c.requesterEmail}) ·`}</span>
                  <Link component={RouterLink} to={`/requests/${c.requestId}`}>
                    request #{c.requestId}
                  </Link>
                </Stack>
              }
            />
          </ListItem>
        ))}
      </List>
      {cancelling && (
        <CancelRequestDialog
          requestId={cancelling.requestId}
          message={`Cancel request #${cancelling.requestId} by ${cancelling.requesterName} (${formatCampusTimeRange(cancelling.start, cancelling.end)})? Its booking is released.`}
          defaultReason={clashCancelReason(blackoutReason)}
          onClose={() => setCancelling(null)}
        />
      )}
    </>
  )
}
