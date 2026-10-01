import { Link as RouterLink } from 'react-router'
import { Box, Card, CardActionArea, CardContent, Typography } from '@mui/material'
import { NAV_ITEMS } from '../../layout/navItems'
import { Roles } from '../../auth/roles'

const BLURBS: Record<string, string> = {
  '/users': 'Create accounts and set roles.',
  '/clubs': 'Clubs and their representatives.',
  '/audit-logs': 'Who changed what, and when.',
}

/**
 * An Admin's landing page: shortcuts to the Admin pages. It makes no API calls; the booking, approval and agent figures
 * on the officer dashboard come from endpoints that refuse Admins.
 */
export function AdminHome() {
  const items = NAV_ITEMS.filter((i) => i.path !== '/' && (i.roles as readonly string[]).includes(Roles.Admin))
  return (
    <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: 'repeat(3, 1fr)' } }}>
      {items.map((item) => (
        <Card key={item.path} variant="outlined">
          <CardActionArea component={RouterLink} to={item.path}>
            <CardContent>
              <Typography variant="subtitle1" component="h2">
                {item.label}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {BLURBS[item.path] ?? ''}
              </Typography>
            </CardContent>
          </CardActionArea>
        </Card>
      ))}
    </Box>
  )
}
