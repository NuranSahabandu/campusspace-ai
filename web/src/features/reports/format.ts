import type { UtilizationFigures } from '../../api/types'
import { formatPercent } from '../agentRuns/format'

const hoursFormat = new Intl.NumberFormat('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 2 })
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/** The most days a report range may cover (the API answers 400 above it). */
export const MAX_RANGE_DAYS = 366

/** Hours with one or two decimals: "12.5 h". */
export const formatHours = (hours: number): string => `${hoursFormat.format(hours)} h`

/**
 * Utilization with its denominator, always: "62.5% (50.0 of 80.0 h)". With nothing available the server's rate is null
 * and this shows "— (0 h available)", never 0%.
 */
export const formatUtilization = (f: UtilizationFigures): string =>
  f.availableHours === 0
    ? '— (0 h available)'
    : `${formatPercent(f.utilization)} (${hoursFormat.format(f.bookedHours)} of ${formatHours(f.availableHours)})`

/** A campus date as a short axis label: "25 Sep" (from the string, never new Date()). */
export const shortDate = (date: string): string => {
  const [, month, day] = date.split('-').map(Number)
  return `${day} ${MONTHS[month - 1]}`
}

/** Days in [from, to], inclusive; both are yyyy-MM-dd. */
export const rangeDays = (from: string, to: string): number =>
  Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000) + 1

/** "09:00" for an hour of the day. */
export const hourLabel = (hour: number): string => `${String(hour).padStart(2, '0')}:00`
