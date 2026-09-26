import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation, useNavigate, useSearchParams } from 'react-router'
import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, Paper, Stack, TextField, Typography } from '@mui/material'
import { z } from 'zod'
import { api, LOGIN_PATH } from '../../api/client'
import { applyFieldErrors, parseProblem } from '../../api/problem'
import type { AuthResponse } from '../../api/types'
import { useAuthStore } from '../../auth/authStore'
import { isStaffRole } from '../../auth/roles'
import { returnPathFor } from '../../auth/routeAccess'

// Mirrors LoginRequest's data annotations; the server remains the real validator (§12).
const schema = z.object({
  email: z.email('Enter a valid email address').max(256),
  password: z.string().min(1, 'Password is required').max(100),
})

type LoginForm = z.infer<typeof schema>

export const INVALID_CREDENTIALS = 'Invalid email or password'
export const NOT_STAFF = 'This portal is for staff. Please use the CampusSpace mobile app.'

export function LoginPage() {
  const token = useAuthStore((s) => s.token)
  const role = useAuthStore((s) => s.user?.role)
  const login = useAuthStore((s) => s.login)
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LoginForm>({ resolver: zodResolver(schema), defaultValues: { email: '', password: '' } })

  // The page to return to, used only if the signed-in role may open it (another user may be signing in).
  const from = (location.state as { from?: string } | null)?.from

  if (token && role) return <Navigate to={returnPathFor(from, role)} replace />

  const onSubmit = async (values: LoginForm) => {
    setFormError(null)
    try {
      const { data } = await api.post<AuthResponse>(LOGIN_PATH, values)
      // Requesters and technicians use Flutter; never keep their token in the staff portal.
      if (!isStaffRole(data.user.role)) {
        setFormError(NOT_STAFF)
        return
      }
      login(data)
      navigate(returnPathFor(from, data.user.role), { replace: true })
    } catch (error) {
      const problem = parseProblem(error)
      if (problem.status === 401) setFormError(INVALID_CREDENTIALS)
      else if (!applyFieldErrors(problem.fieldErrors, setError, ['email', 'password'])) setFormError(problem.title)
    }
  }

  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2, bgcolor: 'grey.100' }}>
      <Paper sx={{ p: 4, width: '100%', maxWidth: 400 }} elevation={3}>
        <Typography variant="h5" component="h1" gutterBottom>
          CampusSpace AI
        </Typography>
        <Typography color="text.secondary" sx={{ mb: 3 }}>
          Staff sign in
        </Typography>

        <Stack component="form" spacing={2} noValidate onSubmit={handleSubmit(onSubmit)}>
          {searchParams.get('expired') === '1' && !formError && (
            <Alert severity="info">Your session has expired. Please sign in again.</Alert>
          )}
          {formError && <Alert severity="error">{formError}</Alert>}
          <TextField
            label="Email"
            type="email"
            autoComplete="email"
            autoFocus
            {...register('email')}
            error={!!errors.email}
            helperText={errors.email?.message}
          />
          <TextField
            label="Password"
            type="password"
            autoComplete="current-password"
            {...register('password')}
            error={!!errors.password}
            helperText={errors.password?.message}
          />
          <Button type="submit" variant="contained" size="large" disabled={isSubmitting}>
            {isSubmitting ? 'Signing in…' : 'Sign in'}
          </Button>
        </Stack>
      </Paper>
    </Box>
  )
}
