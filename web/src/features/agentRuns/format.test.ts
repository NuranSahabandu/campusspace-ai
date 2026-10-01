import { formatMs, formatPercent, formatRate, formatTokens } from './format'

describe('formatMs', () => {
  it('shows ms below a second and seconds with one decimal above', () => {
    expect(formatMs(0)).toBe('0 ms')
    expect(formatMs(850)).toBe('850 ms')
    expect(formatMs(1000)).toBe('1.0 s')
    expect(formatMs(12_449)).toBe('12.4 s')
    expect(formatMs(162_075)).toBe('162.1 s')
  })

  it('shows a dash when there is no value', () => {
    expect(formatMs(null)).toBe('—')
    expect(formatMs(undefined)).toBe('—')
  })
})

describe('formatTokens', () => {
  it('groups thousands', () => {
    expect(formatTokens(17_811)).toBe('17,811')
    expect(formatTokens(5937.3)).toBe('5,937')
    expect(formatTokens(null)).toBe('—')
  })
})

describe('formatRate', () => {
  it('shows the percentage with its denominator', () => {
    expect(formatRate(0.75, 15, 20)).toBe('75.0% (15 of 20)')
    expect(formatRate(0.6667, 2, 3)).toBe('66.7% (2 of 3)')
    expect(formatRate(0, 0, 4)).toBe('0.0% (0 of 4)')
  })

  it('never shows 0% for an empty denominator', () => {
    expect(formatRate(null, 0, 0)).toBe('— (0 of 0)')
    expect(formatPercent(null)).toBe('—')
  })
})
