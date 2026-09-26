/** Campus time zone. Sri Lanka has no daylight saving, so the offset is fixed. */
export const CAMPUS_TIME_ZONE = 'Asia/Colombo'
const CAMPUS_UTC_OFFSET = '+05:30'

const dateTimeFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: CAMPUS_TIME_ZONE,
  dateStyle: 'medium',
  timeStyle: 'short',
})

/** API UTC timestamp (ISO-8601) → campus local time, for example "26 Sept 2026, 14:30". */
export const formatDateTime = (iso: string): string => dateTimeFormat.format(new Date(iso))

/**
 * A native date input's YYYY-MM-DD → the inclusive start/end instants of that day in campus time,
 * with an offset as the API's DateTimeOffset filters expect.
 */
export const campusDayBounds = (date: string) => ({
  from: `${date}T00:00:00${CAMPUS_UTC_OFFSET}`,
  to: `${date}T23:59:59.999${CAMPUS_UTC_OFFSET}`,
})

/**
 * A native datetime-local input's YYYY-MM-DDTHH:mm (read as campus time) → an ISO instant with the campus offset,
 * for example "2026-09-28T08:00:00+05:30". Seconds in the input, if any, are dropped.
 */
export const campusLocalToIso = (value: string) => `${value.slice(0, 16)}:00${CAMPUS_UTC_OFFSET}`
