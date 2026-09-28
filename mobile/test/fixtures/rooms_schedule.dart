// Real API responses (GET /api/rooms/{id}/schedule), captured as kavindi@ on 28 Sep 2026. A101's "Booked" rows came
// from temporary Confirmed bookings inserted with psql and removed after the capture; the blackout is the seeded one.

/// GET /api/rooms/3/schedule?date=2026-09-28: A101 on a Monday with the seeded blackout (08:00–12:00) and a booking
/// (14:00–16:00).
const scheduleBusyJson = r'''
{
  "date": "2026-09-28",
  "open": "08:00",
  "close": "20:00",
  "granularityMinutes": 30,
  "busy": [
    {
      "start": "2026-09-28T02:30:00Z",
      "end": "2026-09-28T06:30:00Z",
      "kind": "Blackout",
      "label": "Projector maintenance"
    },
    {
      "start": "2026-09-28T08:30:00Z",
      "end": "2026-09-28T10:30:00Z",
      "kind": "Booking",
      "label": "Booked"
    }
  ],
  "free": [
    {
      "start": "2026-09-28T06:30:00Z",
      "end": "2026-09-28T08:30:00Z"
    },
    {
      "start": "2026-09-28T10:30:00Z",
      "end": "2026-09-28T14:30:00Z"
    }
  ]
}
''';

/// GET /api/rooms/3/schedule?date=2026-10-04: a Sunday, closed.
const scheduleClosedJson = r'''
{
  "date": "2026-10-04",
  "open": null,
  "close": null,
  "granularityMinutes": 30,
  "busy": [],
  "free": []
}
''';

/// GET /api/rooms/3/schedule?date=2026-10-06: one booking covers the opening hours (08:00–20:00).
const scheduleFullJson = r'''
{
  "date": "2026-10-06",
  "open": "08:00",
  "close": "20:00",
  "granularityMinutes": 30,
  "busy": [
    {
      "start": "2026-10-06T02:30:00Z",
      "end": "2026-10-06T14:30:00Z",
      "kind": "Booking",
      "label": "Booked"
    }
  ],
  "free": []
}
''';
