import { Chip } from '@mui/material'
import { requestStatusChipStyle, requestStatusLabel } from './requestStatus'

/** A small chip with the status's friendly label and colour. */
export function RequestStatusChip({ status, size = 'small' }: { status: string; size?: 'small' | 'medium' }) {
  const { color, variant } = requestStatusChipStyle(status)
  return <Chip size={size} color={color} variant={variant} label={requestStatusLabel(status)} />
}
