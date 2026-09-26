import type { ReactNode } from 'react'
import { CssBaseline, ThemeProvider } from '@mui/material'
import { type QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { theme } from './theme'
import { Toaster } from './ui/Toaster'

/** Everything except the router, so tests can supply a MemoryRouter. */
export function AppProviders({ client, children }: { client: QueryClient; children: ReactNode }) {
  return (
    <QueryClientProvider client={client}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
        <Toaster />
      </ThemeProvider>
    </QueryClientProvider>
  )
}
