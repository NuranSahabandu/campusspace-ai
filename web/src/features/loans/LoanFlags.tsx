import { Chip, Stack } from '@mui/material'
import type { LoanDto } from '../../api/types'

/** Overdue, late-return and damaged flags, shared by the grid and the drawer. */
export function LoanFlags({ loan }: { loan: LoanDto }) {
  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', height: '100%' }}>
      {loan.isOverdue && <Chip size="small" color="error" label="Overdue" />}
      {loan.isLateReturn && <Chip size="small" color="warning" variant="outlined" label="Late return" />}
      {loan.returnCondition === 'Damaged' && <Chip size="small" color="error" variant="outlined" label="Damaged" />}
    </Stack>
  )
}
