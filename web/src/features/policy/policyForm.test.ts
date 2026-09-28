import { DEFAULT_OPENING_HOURS, POLICY_SETTINGS } from '../../test/fixtures'
import { changedKeys, describeChanges, policySchema, serialize, toFormValues } from './policyForm'

const loaded = () => toFormValues(POLICY_SETTINGS)
const issues = (form: ReturnType<typeof loaded>) => {
  const result = policySchema.safeParse(form)
  return result.success ? [] : result.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`)
}

describe('policyForm', () => {
  it('parses the stored values into typed form values', () => {
    const form = loaded()
    expect(form.opening_hours.mon).toEqual({ isOpen: true, open: '08:00', close: '20:00' })
    expect(form.opening_hours.sun).toEqual({ isOpen: false, open: '', close: '' })
    expect(form.max_capacity_ratio).toBe('3')
  })

  it('serializes back to exactly the stored text', () => {
    const values = serialize(loaded())
    expect(values.opening_hours).toBe(DEFAULT_OPENING_HOURS)
    expect(Object.fromEntries(POLICY_SETTINGS.map((s) => [s.key, s.value]))).toEqual(values)
  })

  it('writes numbers the way the server stores them', () => {
    const form = { ...loaded(), max_capacity_ratio: '2.50', max_duration_hours: '08' }
    expect(serialize(form)).toMatchObject({ max_capacity_ratio: '2.5', max_duration_hours: '8' })
    expect(serialize({ ...form, max_capacity_ratio: '3.0' }).max_capacity_ratio).toBe('3')
  })

  it('reports only keys whose stored text changes', () => {
    const baseline = serialize(loaded())
    expect(changedKeys(baseline, { ...loaded(), max_capacity_ratio: '3.0' })).toEqual([])
    const form = { ...loaded(), max_capacity_ratio: '2' }
    form.opening_hours = { ...form.opening_hours, sat: { ...form.opening_hours.sat, isOpen: false } }
    const keys = changedKeys(baseline, form)
    expect(keys).toEqual(['opening_hours', 'max_capacity_ratio'])
    expect(describeChanges(loaded(), form, keys)).toEqual([
      'Opening hours, Sat: 08:00–16:00 → closed',
      'Capacity ratio: 3 → 2',
    ])
  })

  it('accepts the defaults', () => {
    expect(issues(loaded())).toEqual([])
  })

  it('mirrors the server checks across keys', () => {
    const form = loaded()
    form.slot_granularity_minutes = '60'
    form.opening_hours = {
      ...form.opening_hours,
      sat: { isOpen: true, open: '08:30', close: '16:00' },
      fri: { isOpen: true, open: '20:00', close: '08:00' },
    }
    form.max_duration_hours = '13'
    expect(issues(form)).toEqual([
      'opening_hours.fri.close: Friday: opening time must be before closing time.',
      'opening_hours.sat.open: Saturday: times must be on a 60-minute boundary.',
      "max_duration_hours: Can't be longer than the longest open day (12 h).",
    ])
  })

  it('needs one open day and checks ranges and decimals', () => {
    const form = loaded()
    form.opening_hours = Object.fromEntries(
      Object.entries(form.opening_hours).map(([day, hours]) => [day, { ...hours, isOpen: false }]),
    ) as typeof form.opening_hours
    form.max_capacity_ratio = '2.55'
    form.max_open_requests = '21'
    form.min_lead_time_hours = '1.5'
    form.checkout_window_minutes = '241'
    expect(issues(form)).toEqual([
      'min_lead_time_hours: Must be a whole number.',
      'max_capacity_ratio: Can have at most 1 decimal place.',
      'max_open_requests: Must be between 1 and 20.',
      'checkout_window_minutes: Must be between 0 and 240.',
      'opening_hours: At least one day must be open.',
    ])
  })
})
