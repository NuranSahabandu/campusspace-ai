import { Alert, AlertTitle, Box } from '@mui/material'
import { policyLabel } from '../policy/policyForm'

/**
 * Addendum: "policy changed since this proposal". The keys come from the server (policyChangedKeys: current value !=
 * the run's snapshot), shown in plain words with the Booking policy page's labels.
 */
export function PolicyChangedBanner({ keys }: { keys: string[] }) {
  if (!keys.length) return null
  return (
    <Alert severity="warning" aria-label="Policy changed">
      <AlertTitle>The booking policy changed since this proposal</AlertTitle>
      The agents checked the request against the old values of:
      <Box component="ul" sx={{ my: 0.5, pl: 2.5 }}>
        {keys.map((key) => (
          <li key={key}>{policyLabel(key)}</li>
        ))}
      </Box>
      Approval re-checks the time rules against the current policy; ask for a revision if the proposal depends on the change.
    </Alert>
  )
}
