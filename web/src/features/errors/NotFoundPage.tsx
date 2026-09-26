import { Box, Button, Typography } from '@mui/material'
import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <Box sx={{ textAlign: 'center', mt: 8 }}>
      <Typography variant="h3" component="h1" gutterBottom>
        404
      </Typography>
      <Typography sx={{ mb: 3 }}>Page not found.</Typography>
      <Button component={Link} to="/" variant="contained">
        Back to dashboard
      </Button>
    </Box>
  )
}
