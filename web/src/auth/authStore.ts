import { create } from 'zustand'
import { createJSONStorage, persist } from 'zustand/middleware'
import type { AuthResponse, UserDto } from '../api/types'

// Token storage (§15.2 baseline): the access token lives in localStorage so a page refresh keeps the session.
// Trade-off for the ADR: any script running on the page (an XSS bug or a malicious dependency) can read it.
// The stretch goal replaces this with an in-memory token plus an HttpOnly refresh cookie.

export const AUTH_STORAGE_KEY = 'campusspace-auth'

interface AuthState {
  token: string | null
  expiresAt: string | null
  user: UserDto | null
  login: (response: AuthResponse) => void
  logout: () => void
}

const isExpired = (expiresAt: string | null) => !expiresAt || Date.parse(expiresAt) <= Date.now()

let logoutTimer: ReturnType<typeof setTimeout> | undefined

// Log out automatically when the token expires, instead of waiting for the next API call to 401.
function scheduleLogout(expiresAt: string | null) {
  clearTimeout(logoutTimer)
  if (!expiresAt) return
  // setTimeout overflows above ~24.8 days; tokens last hours, but clamp anyway.
  const delay = Math.min(Math.max(Date.parse(expiresAt) - Date.now(), 0), 2 ** 31 - 1)
  logoutTimer = setTimeout(() => useAuthStore.getState().logout(), delay)
}

const empty = { token: null, expiresAt: null, user: null }

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      ...empty,
      login: ({ accessToken, expiresAt, user }) => {
        set({ token: accessToken, expiresAt, user })
        scheduleLogout(expiresAt)
      },
      logout: () => {
        clearTimeout(logoutTimer)
        set(empty)
      },
    }),
    {
      name: AUTH_STORAGE_KEY,
      storage: createJSONStorage(() => localStorage),
      partialize: ({ token, expiresAt, user }) => ({ token, expiresAt, user }),
      onRehydrateStorage: () => (state) => {
        if (!state?.token) return
        if (isExpired(state.expiresAt)) state.logout()
        else scheduleLogout(state.expiresAt)
      },
    },
  ),
)
