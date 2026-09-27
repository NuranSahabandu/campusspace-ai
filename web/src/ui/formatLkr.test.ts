import { formatLkr } from './formatLkr'

describe('formatLkr', () => {
  it('always shows two decimal places', () => {
    expect(formatLkr(0)).toBe('LKR 0.00')
    expect(formatLkr(500)).toBe('LKR 500.00')
    expect(formatLkr(12.5)).toBe('LKR 12.50')
  })

  it('groups thousands', () => {
    expect(formatLkr(1500)).toBe('LKR 1,500.00')
  })

  it('handles the largest fee the server accepts', () => {
    expect(formatLkr(99999999.99)).toBe('LKR 99,999,999.99')
  })
})
