// Mirrors backend/CampusSpace.Api/Models/RoomTypes.cs. Keep the two in sync.
export const ROOM_TYPES = ['LectureHall', 'ComputerLab', 'SeminarRoom', 'Auditorium'] as const

export type RoomType = (typeof ROOM_TYPES)[number]

const LABELS: Record<RoomType, string> = {
  LectureHall: 'Lecture hall',
  ComputerLab: 'Computer lab',
  SeminarRoom: 'Seminar room',
  Auditorium: 'Auditorium',
}

/** Display label for a room type; unknown values are shown as they are. */
export const roomTypeLabel = (type: string) => LABELS[type as RoomType] ?? type
