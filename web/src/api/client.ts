import axios from 'axios'
import { useAuthStore } from '../auth/authStore'

export const LOGIN_PATH = '/api/auth/login'

/** Full-page navigation, behind an object so tests can spy on it (jsdom cannot navigate). */
export const browser = {
  assign: (url: string) => window.location.assign(url),
}

/** The only HTTP client. Clients call ASP.NET Core /api/... only (§7.1 rule 1). */
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
})

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

api.interceptors.response.use(
  (response) => response,
  (error) => {
    // A 401 from login means wrong credentials; anywhere else it means the session is gone.
    if (axios.isAxiosError(error) && error.response?.status === 401 && error.config?.url !== LOGIN_PATH) {
      useAuthStore.getState().logout()
      browser.assign('/login?expired=1')
    }
    return Promise.reject(error)
  },
)
