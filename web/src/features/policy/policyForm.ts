import { z } from 'zod'
import type { PolicySettingDto } from '../../api/types'

// Mirrors PolicyKeys and PolicyRules (backend/CampusSpace.Api). Keep them in sync. The form field names ARE the setting
// keys: parseProblem only lower-cases the first letter, so server errors keyed "max_duration_hours" land on them as-is.

export const DAYS = [
  { key: 'mon', label: 'Monday', short: 'Mon' },
  { key: 'tue', label: 'Tuesday', short: 'Tue' },
  { key: 'wed', label: 'Wednesday', short: 'Wed' },
  { key: 'thu', label: 'Thursday', short: 'Thu' },
  { key: 'fri', label: 'Friday', short: 'Fri' },
  { key: 'sat', label: 'Saturday', short: 'Sat' },
  { key: 'sun', label: 'Sunday', short: 'Sun' },
] as const
export type Day = (typeof DAYS)[number]['key']

export const OPENING_HOURS = 'opening_hours'

/** The number keys in PolicyKeys.All order, with the page's label and unit. */
export const NUMBER_FIELDS = [
  { key: 'min_lead_time_hours', label: 'Minimum lead time', unit: 'hours' },
  { key: 'max_advance_days_student', label: 'Student advance window', unit: 'days' },
  { key: 'max_advance_days_lecturer', label: 'Lecturer advance window', unit: 'days' },
  { key: 'max_duration_hours', label: 'Maximum duration', unit: 'hours' },
  { key: 'max_capacity_ratio', label: 'Capacity ratio', unit: '× attendees' },
  { key: 'slot_granularity_minutes', label: 'Slot granularity', unit: 'minutes' },
  { key: 'free_cancellation_hours', label: 'Free cancellation', unit: 'hours' },
  { key: 'max_open_requests', label: 'Open requests per requester', unit: 'requests' },
] as const
export type NumberKey = (typeof NUMBER_FIELDS)[number]['key']

/** PolicyRules.AllowedGranularities. */
export const GRANULARITIES = ['15', '30', '60'] as const

export interface DayForm {
  isOpen: boolean
  /** "HH:mm"; blank while a closed day has never had times. */
  open: string
  close: string
}

export type PolicyForm = { opening_hours: Record<Day, DayForm> } & Record<NumberKey, string>
export type PolicyKey = typeof OPENING_HOURS | NumberKey

export const POLICY_FIELDS: readonly PolicyKey[] = [OPENING_HOURS, ...NUMBER_FIELDS.map((f) => f.key)]

export const policyLabel = (key: string) =>
  key === OPENING_HOURS ? 'Opening hours' : (NUMBER_FIELDS.find((f) => f.key === key)?.label ?? key)

/** Times a closed day gets when it is switched on and has none yet. */
export const DEFAULT_OPEN = '08:00'
export const DEFAULT_CLOSE = '17:00'

type StoredHours = Record<string, { open: string; close: string } | null>

/** The loaded settings → typed form values (parsed once). Throws if opening_hours is not the stored JSON shape. */
export function toFormValues(settings: readonly PolicySettingDto[]): PolicyForm {
  const values = Object.fromEntries(settings.map((s) => [s.key, s.value]))
  const hours = JSON.parse(values[OPENING_HOURS]) as StoredHours
  const opening_hours = Object.fromEntries(
    DAYS.map(({ key }) => {
      const day = hours[key]
      return [key, day ? { isOpen: true, open: day.open, close: day.close } : { isOpen: false, open: '', close: '' }]
    }),
  ) as Record<Day, DayForm>
  const numbers = Object.fromEntries(NUMBER_FIELDS.map(({ key }) => [key, values[key] ?? ''])) as Record<NumberKey, string>
  return { opening_hours, ...numbers }
}

// "08" → "8", "2.50" → "2.5", "3.0" → "3": the text the server stores (PolicyRules.ToValues). Invalid text is kept,
// so it still counts as a change and validation reports it.
const numberText = (raw: string) => {
  const value = raw.trim()
  const n = Number(value)
  return value !== '' && Number.isFinite(n) ? String(n) : value
}

/** Compact JSON in mon..sun order, null for a closed day: exactly how the server stores opening_hours. */
export const openingHoursText = (hours: Record<Day, DayForm>) =>
  JSON.stringify(
    Object.fromEntries(
      DAYS.map(({ key }) => {
        const d = hours[key]
        return [key, d.isOpen ? { open: d.open.slice(0, 5), close: d.close.slice(0, 5) } : null]
      }),
    ),
  )

/** Form values → key → the value text the server stores. */
export function serialize(form: PolicyForm): Record<PolicyKey, string> {
  const numbers = Object.fromEntries(NUMBER_FIELDS.map(({ key }) => [key, numberText(form[key])])) as Record<NumberKey, string>
  return { [OPENING_HOURS]: openingHoursText(form.opening_hours), ...numbers }
}

/** Keys whose stored text would change. Editing a value and changing it back is not a change. */
export const changedKeys = (baseline: Record<PolicyKey, string>, form: PolicyForm): PolicyKey[] => {
  const current = serialize(form)
  return POLICY_FIELDS.filter((key) => current[key] !== baseline[key])
}

const dayText = (d: DayForm) => (d.isOpen ? `${d.open.slice(0, 5)}–${d.close.slice(0, 5)}` : 'closed')

/** Human-readable lines for the confirm dialog, for example "Capacity ratio: 3 → 2". */
export function describeChanges(baseline: PolicyForm, form: PolicyForm, keys: readonly PolicyKey[]): string[] {
  return keys.flatMap((key) => {
    if (key !== OPENING_HOURS) return [`${policyLabel(key)}: ${numberText(baseline[key])} → ${numberText(form[key])}`]
    return DAYS.filter(({ key: day }) => dayText(baseline.opening_hours[day]) !== dayText(form.opening_hours[day])).map(
      ({ key: day, short }) => `Opening hours, ${short}: ${dayText(baseline.opening_hours[day])} → ${dayText(form.opening_hours[day])}`,
    )
  })
}

const minutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))
const TIME = /^([01]\d|2[0-3]):[0-5]\d/

// PolicyRules.Range: whole numbers within [min, max], same messages.
const intField = (min: number, max: number) =>
  z.string().superRefine((raw, ctx) => {
    const value = raw.trim()
    if (!/^-?\d+$/.test(value)) return ctx.addIssue({ code: 'custom', message: 'Must be a whole number.' })
    const n = Number(value)
    if (n < min || n > max) ctx.addIssue({ code: 'custom', message: `Must be between ${min} and ${max}.` })
  })

const dayField = z.object({ isOpen: z.boolean(), open: z.string(), close: z.string() })

/**
 * Mirrors PolicyRules.Parse and PolicyRules.Validate, including the checks across keys (times on the form's granularity,
 * duration within the longest open day). The server checks the whole policy again and remains the real validator.
 */
export const policySchema = z
  .object({
    opening_hours: z.object(Object.fromEntries(DAYS.map(({ key }) => [key, dayField])) as Record<Day, typeof dayField>),
    min_lead_time_hours: intField(0, 720),
    max_advance_days_student: intField(1, 365),
    max_advance_days_lecturer: intField(1, 365),
    max_duration_hours: intField(1, 24),
    max_capacity_ratio: z.string().superRefine((raw, ctx) => {
      const value = raw.trim()
      const n = Number(value)
      if (value === '' || !Number.isFinite(n)) return ctx.addIssue({ code: 'custom', message: 'Must be a number.' })
      if (n < 1 || n > 10) return ctx.addIssue({ code: 'custom', message: 'Must be between 1 and 10.' })
      if (Math.round(n * 10) / 10 !== n) ctx.addIssue({ code: 'custom', message: 'Can have at most 1 decimal place.' })
    }),
    slot_granularity_minutes: z
      .string()
      .refine((g) => (GRANULARITIES as readonly string[]).includes(g), `Must be one of: ${GRANULARITIES.join(', ')}.`),
    free_cancellation_hours: intField(0, 720),
    max_open_requests: intField(1, 20),
  })
  .superRefine((form, ctx) => {
    const granularity = Number(form.slot_granularity_minutes)
    // Like the server, check boundaries only against a valid granularity (its own refine reports a bad one).
    const granularityOk = (GRANULARITIES as readonly string[]).includes(form.slot_granularity_minutes)
    const issue = (path: (string | number)[], message: string) => ctx.addIssue({ code: 'custom', path, message })

    const openDays: number[] = []
    for (const { key, label } of DAYS) {
      const day = form.opening_hours[key]
      if (!day.isOpen) continue
      const path = (field: 'open' | 'close') => [OPENING_HOURS, key, field]
      if (!TIME.test(day.open)) issue(path('open'), `${label}: enter an opening time.`)
      if (!TIME.test(day.close)) issue(path('close'), `${label}: enter a closing time.`)
      if (!TIME.test(day.open) || !TIME.test(day.close)) continue

      const [open, close] = [minutes(day.open), minutes(day.close)]
      if (open >= close) issue(path('close'), `${label}: opening time must be before closing time.`)
      else openDays.push(close - open)
      for (const field of ['open', 'close'] as const) {
        if (granularityOk && minutes(day[field]) % granularity !== 0)
          issue(path(field), `${label}: times must be on a ${granularity}-minute boundary.`)
      }
    }
    if (DAYS.every(({ key }) => !form.opening_hours[key].isOpen)) issue([OPENING_HOURS], 'At least one day must be open.')

    const duration = Number(form.max_duration_hours.trim())
    if (!/^\d+$/.test(form.max_duration_hours.trim()) || duration < 1 || duration > 24) return
    if (granularityOk && (duration * 60) % granularity !== 0)
      issue(['max_duration_hours'], `Must be a multiple of ${granularity} minutes.`)
    const longest = Math.max(0, ...openDays)
    if (openDays.length > 0 && duration * 60 > longest)
      issue(['max_duration_hours'], `Can't be longer than the longest open day (${Number((longest / 60).toFixed(2))} h).`)
  })
