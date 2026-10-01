import WarningAmberIcon from '@mui/icons-material/WarningAmber'
import {
  Chip,
  Paper,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import type { CurrentPricingRuleDto } from '../../api/types'
import { formatDateOnly } from '../../ui/formatDateTime'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { formatRate, REQUESTER_ROLES, roomTypeLabel } from './pricingValues'
import { useCurrentPrices } from './usePricing'

function PriceCell({ rule }: { rule: CurrentPricingRuleDto | undefined }) {
  if (!rule || rule.hourlyRate === null || rule.validFrom === null) {
    return <Chip icon={<WarningAmberIcon />} label="No rule" color="warning" size="small" variant="outlined" />
  }
  return (
    <>
      <Typography variant="body2">{formatRate({ hourlyRate: rule.hourlyRate, isExempt: !!rule.isExempt })}</Typography>
      <Typography variant="caption" color="text.secondary">
        from {formatDateOnly(rule.validFrom)}
      </Typography>
    </>
  )
}

/** The rule in effect today for each room type and requester role (GET /api/pricing-rules/current). */
export function CurrentPricesCard() {
  const { data, isPending, isError, error, refetch } = useCurrentPrices()

  let body
  if (isError) {
    body = (
      <QueryErrorAlert error={error} what="current prices" onRetry={() => refetch()} />
    )
  } else if (isPending) {
    body = <Skeleton variant="rectangular" height={180} aria-label="Loading current prices" />
  } else {
    // The API returns the room types in order, each with one row per requester role.
    const roomTypes = [...new Set(data.map((r) => r.roomType))]
    body = (
      <TableContainer>
        <Table size="small" aria-label="Current prices">
          <TableHead>
            <TableRow>
              <TableCell>Room type</TableCell>
              {REQUESTER_ROLES.map((role) => (
                <TableCell key={role}>{role}</TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {roomTypes.map((roomType) => (
              <TableRow key={roomType}>
                <TableCell component="th" scope="row">
                  {roomTypeLabel(roomType)}
                </TableCell>
                {REQUESTER_ROLES.map((role) => (
                  <TableCell key={role}>
                    <PriceCell rule={data.find((r) => r.roomType === roomType && r.requesterRole === role)} />
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    )
  }

  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
      <Typography variant="h6" component="h2" gutterBottom>
        Current prices
      </Typography>
      {body}
    </Paper>
  )
}
