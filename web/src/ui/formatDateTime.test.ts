import { campusDayBounds, formatDateTime } from './formatDateTime'

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
