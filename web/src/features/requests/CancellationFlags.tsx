import { Chip, Stack } from '@mui/material'

interface Props {
  isLateCancellation: boolean
  cancelledByOfficer: boolean
}

/**
 * Small chips for how a request was cancelled: "Late" (an owner's late cancellation of an approved booking, flagged,
 * not charged) and "By office" (a Facilities Officer cancelled it). Renders nothing when neither applies.
 */
export function CancellationFlags({ isLateCancellation, cancelledByOfficer }: Props) {
  if (!isLateCancellation && !cancelledByOfficer) return null
  return (
    <Stack direction="row" spacing={0.5}>
      {isLateCancellation && <Chip size="small" color="warning" variant="outlined" label="Late" />}
      {cancelledByOfficer && <Chip size="small" variant="outlined" label="By office" />}
    </Stack>
  )
}
