import type { RoomUtilizationDto } from '../../api/types'

export type SortKey = 'room' | 'building' | 'booked' | 'available' | 'utilization'
export type Direction = 'asc' | 'desc'

const value = (r: RoomUtilizationDto, key: SortKey): string | number | null => {
  switch (key) {
    case 'room':
      return r.code
    case 'building':
      return r.buildingCode
    case 'booked':
      return r.figures.bookedHours
    case 'available':
      return r.figures.availableHours
    case 'utilization':
      return r.figures.utilization
  }
}

/** Rooms sorted on the client (the report holds every room); rooms with no utilization (null) always sort last. */
export function sortRooms(rooms: RoomUtilizationDto[], key: SortKey, direction: Direction): RoomUtilizationDto[] {
  const sign = direction === 'asc' ? 1 : -1
  return [...rooms].sort((a, b) => {
    const x = value(a, key)
    const y = value(b, key)
    if (x === null || y === null) return x === y ? a.code.localeCompare(b.code) : x === null ? 1 : -1
    const c = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y))
    return c !== 0 ? sign * c : a.code.localeCompare(b.code)
  })
}
