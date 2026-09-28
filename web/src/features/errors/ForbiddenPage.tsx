import { Box, Button, Stack, Typography } from '@mui/material'
import { Link, useNavigate } from 'react-router'
import { useAuthStore } from '../../auth/authStore'
import { isStaffRole, NOT_STAFF_MESSAGE } from '../../auth/roles'

/**
 * The access-denied page. Any signed-in user can reach it (App.tsx keeps it outside the staff guard), so a requester or
 * technician with a session lands here instead of looping. They get the mobile-app message and no dashboard link,
 * since the dashboard would forbid them again. Everyone can sign out.
 */
export function ForbiddenPage() {
  const role = useAuthStore((s) => s.user?.role)
  const logout = useAuthStore((s) => s.logout)
  const navigate = useNavigate()
  const staff = role !== undefined && isStaffRole(role)

  const signOut = () => {
    // Same as the account menu: an explicit logout starts the next login with no page to return to.
    logout({ byUser: true })
    navigate('/login', { replace: true })
  }

  return (
    <Box sx={{ textAlign: 'center', mt: 8 }}>
      <Typography variant="h3" component="h1" gutterBottom>
        403
      </Typography>
      <Typography sx={{ mb: 3 }}>{staff ? 'You do not have permission to view this page.' : NOT_STAFF_MESSAGE}</Typography>
      <Stack direction="row" spacing={2} sx={{ justifyContent: 'center' }}>
        {staff && (
          <Button component={Link} to="/" variant="contained">
            Back to dashboard
          </Button>
        )}
        <Button variant={staff ? 'outlined' : 'contained'} onClick={signOut}>
          Sign out
        </Button>
      </Stack>
    </Box>
  )
}
