import type { RoomUtilizationDto } from '../../api/types'
import { formatHours, formatUtilization, hourLabel, rangeDays, shortDate } from './format'
import { sortRooms } from './roomSort'

const room = (code: string, booked: number, available: number, utilization: number | null): RoomUtilizationDto => ({
  roomId: code.charCodeAt(0),
  code,
  name: code,
  buildingId: 1,
  buildingCode: 'MB',
  isActive: true,
  figures: { bookedHours: booked, availableHours: available, utilization },
})

describe('report formatting', () => {
  it('shows utilization with its hours, and "—" when nothing was available', () => {
    expect(formatUtilization({ bookedHours: 50, availableHours: 80, utilization: 0.625 })).toBe('62.5% (50.0 of 80.0 h)')
    expect(formatUtilization({ bookedHours: 0, availableHours: 0, utilization: null })).toBe('— (0 h available)')
    expect(formatHours(1288)).toBe('1,288.0 h')
    expect(formatHours(2.25)).toBe('2.25 h')
  })

  it('counts range days inclusively across months and leap years', () => {
    expect(rangeDays('2026-09-01', '2026-09-30')).toBe(30)
    expect(rangeDays('2026-09-20', '2026-09-20')).toBe(1)
    expect(rangeDays('2028-01-01', '2028-12-31')).toBe(366)
  })

  it('labels dates and hours without new Date()', () => {
    expect(shortDate('2026-09-05')).toBe('5 Sep')
    expect(hourLabel(9)).toBe('09:00')
  })

  it('sorts rooms with null utilization last in both directions', () => {
    const rooms = [room('B', 1, 10, 0.1), room('A', 0, 0, null), room('C', 5, 10, 0.5)]
    expect(sortRooms(rooms, 'utilization', 'desc').map((r) => r.code)).toEqual(['C', 'B', 'A'])
    expect(sortRooms(rooms, 'utilization', 'asc').map((r) => r.code)).toEqual(['B', 'C', 'A'])
    expect(sortRooms(rooms, 'room', 'asc').map((r) => r.code)).toEqual(['A', 'B', 'C'])
  })
})
