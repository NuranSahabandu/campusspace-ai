import { create } from 'zustand'

export type ToastSeverity = 'success' | 'error' | 'info'

interface ToastState {
  current: { id: number; message: string; severity: ToastSeverity } | null
  show: (message: string, severity: ToastSeverity) => void
  dismiss: () => void
}

let nextId = 1

/** One toast at a time; a new one replaces the old. Enough for success/error feedback (§12). */
export const useToastStore = create<ToastState>()((set) => ({
  current: null,
  show: (message, severity) => set({ current: { id: nextId++, message, severity } }),
  dismiss: () => set({ current: null }),
}))

export const toast = {
  success: (message: string) => useToastStore.getState().show(message, 'success'),
  error: (message: string) => useToastStore.getState().show(message, 'error'),
  info: (message: string) => useToastStore.getState().show(message, 'info'),
}
