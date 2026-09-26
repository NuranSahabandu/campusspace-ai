import { Alert, Typography } from '@mui/material'

interface Props {
  title: string
  /** Component owner from §18, for example "A". */
  owner: string
}

export function ComingSoonPage({ title, owner }: Props) {
  return (
    <>
      <Typography variant="h4" component="h1" gutterBottom>
        {title}
      </Typography>
      <Alert severity="info">Coming in Phase 1 (component {owner}).</Alert>
    </>
  )
}
