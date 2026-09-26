import { Box, Button, Typography } from '@mui/material'
import { Link } from 'react-router'

export function ForbiddenPage() {
  return (
    <Box sx={{ textAlign: 'center', mt: 8 }}>
      <Typography variant="h3" component="h1" gutterBottom>
        403
      </Typography>
      <Typography sx={{ mb: 3 }}>You do not have permission to view this page.</Typography>
      <Button component={Link} to="/" variant="contained">
        Back to dashboard
      </Button>
    </Box>
  )
}
