import { Alert, Snackbar } from '@mui/material'
import { useToastStore } from './toastStore'

export function Toaster() {
  const current = useToastStore((s) => s.current)
  const dismiss = useToastStore((s) => s.dismiss)
  if (!current) return null

  return (
    <Snackbar
      key={current.id}
      open
      autoHideDuration={5000}
      onClose={(_, reason) => reason !== 'clickaway' && dismiss()}
      anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
    >
      <Alert onClose={dismiss} severity={current.severity} variant="filled" sx={{ width: '100%' }}>
        {current.message}
      </Alert>
    </Snackbar>
  )
}
