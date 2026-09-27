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

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/**
 * An API DateOnly ("yyyy-MM-dd", a campus calendar date) for display, for example "1 Jan 2026". Parsed from the text,
 * never through new Date(): that reads a date-only string as UTC midnight, which is the previous day west of UTC.
 */
export const formatDateOnly = (date: string): string => {
  const [year, month, day] = date.split('-').map(Number)
  return `${day} ${MONTHS[month - 1]} ${year}`
}

const campusDateFormat = new Intl.DateTimeFormat('en-CA', {
  timeZone: CAMPUS_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

/** Today's campus date as "yyyy-MM-dd" (the server's CampusTime.Today), for a date input's min and date checks. */
export const campusToday = (): string => {
  const parts = Object.fromEntries(campusDateFormat.formatToParts(new Date()).map((p) => [p.type, p.value]))
  return `${parts.year}-${parts.month}-${parts.day}`
}

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

const campusPartsFormat = new Intl.DateTimeFormat('en-CA', {
  timeZone: CAMPUS_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

/** An instant's campus calendar date and time as numbers, read from Intl parts only (no locale text). */
const campusParts = (iso: string) => {
  const parts = Object.fromEntries(campusPartsFormat.formatToParts(new Date(iso)).map((p) => [p.type, p.value]))
  return {
    year: Number(parts.year),
    month: Number(parts.month),
    day: Number(parts.day),
    time: `${parts.hour}:${parts.minute}`,
  }
}

const campusDayText = ({ year, month, day }: { year: number; month: number; day: number }) =>
  `${WEEKDAYS[new Date(Date.UTC(year, month - 1, day)).getUTCDay()]} ${day} ${MONTHS[month - 1]} ${year}`

/**
 * A UTC start/end pair as a campus time range: "Tue 20 Oct 2026, 14:00–17:00", or with both dates when the range
 * ends on a later campus day: "Tue 20 Oct 2026, 22:00 – Wed 21 Oct 2026, 01:00".
 */
export const formatCampusTimeRange = (startUtc: string, endUtc: string): string => {
  const start = campusParts(startUtc)
  const end = campusParts(endUtc)
  const startDay = campusDayText(start)
  const endDay = campusDayText(end)
  return startDay === endDay
    ? `${startDay}, ${start.time}–${end.time}`
    : `${startDay}, ${start.time} – ${endDay}, ${end.time}`
}
