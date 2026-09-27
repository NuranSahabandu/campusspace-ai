import { campusDayBounds, campusLocalToIso, campusToday, formatDateOnly, formatDateTime } from './formatDateTime'

describe('formatDateTime', () => {
  it('shows a UTC timestamp in Colombo time (UTC+05:30)', () => {
    const text = formatDateTime('2026-09-26T08:45:00Z')
    expect(text).toContain('2026')
    expect(text).toContain('14:15')
  })

  it('rolls over to the next local day', () => {
    expect(formatDateTime('2026-09-26T20:00:00Z')).toMatch(/^27 /)
  })
})

describe('campusDayBounds', () => {
  it('turns a date input into inclusive local-day instants', () => {
    expect(campusDayBounds('2026-09-26')).toEqual({
      from: '2026-09-26T00:00:00+05:30',
      to: '2026-09-26T23:59:59.999+05:30',
    })
  })
})

describe('campusLocalToIso', () => {
  it('reads a datetime-local value as campus time', () => {
    expect(campusLocalToIso('2026-09-28T08:00')).toBe('2026-09-28T08:00:00+05:30')
  })

  it('drops seconds the input may include', () => {
    expect(campusLocalToIso('2026-09-28T23:45:30')).toBe('2026-09-28T23:45:00+05:30')
  })

  it('gives the right UTC instant', () => {
    expect(new Date(campusLocalToIso('2026-09-28T08:00')).toISOString()).toBe('2026-09-28T02:30:00.000Z')
  })
})

describe('formatDateOnly', () => {
  const originalTz = process.env.TZ
  afterEach(() => {
    process.env.TZ = originalTz
  })

  it('shows a DateOnly as day, short month and year', () => {
    expect(formatDateOnly('2026-01-01')).toBe('1 Jan 2026')
    expect(formatDateOnly('2026-11-30')).toBe('30 Nov 2026')
  })

  it('does not shift the date west of UTC', () => {
    // new Date('2026-01-01') would be 31 Dec 2025 here.
    process.env.TZ = 'America/Los_Angeles'
    expect(formatDateOnly('2026-01-01')).toBe('1 Jan 2026')
  })
})

describe('campusToday', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('is already the next day in Colombo at 19:00 UTC', () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-09-27T19:00:00Z'))
    expect(campusToday()).toBe('2026-09-28')
  })

  it('is the same day before 18:30 UTC', () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-09-27T18:29:00Z'))
    expect(campusToday()).toBe('2026-09-27')
  })
})
