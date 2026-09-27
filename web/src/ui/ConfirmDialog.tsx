import type { ReactNode } from 'react'
import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'

interface Props {
  open: boolean
  title: string
  message: string
  confirmLabel: string
  /** Red confirm button for actions that remove or undo something. */
  destructive?: boolean
  /** Disables both buttons while the action runs. */
  pending?: boolean
  onConfirm: () => void
  onClose: () => void
  /** Extra content under the message, for example a list of changes. */
  children?: ReactNode
}

export function ConfirmDialog({ open, title, message, confirmLabel, destructive, pending, onConfirm, onClose, children }: Props) {
  return (
    <Dialog open={open} onClose={pending ? undefined : onClose} aria-labelledby="confirm-dialog-title">
      <DialogTitle id="confirm-dialog-title">{title}</DialogTitle>
      <DialogContent>
        <DialogContentText>{message}</DialogContentText>
        {children}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={pending}>
          Cancel
        </Button>
        <Button
          onClick={onConfirm}
          disabled={pending}
          variant="contained"
          color={destructive ? 'error' : 'primary'}
        >
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
