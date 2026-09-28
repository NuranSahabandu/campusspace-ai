// Bodies captured with curl from the running API as tech@ on 2026-09-28, after the Task 2.7 demo SQL (bookings 9
// and 10). Keep them as the API sends them: tests use them to check the Dart models match the backend DTOs.

/// GET /api/loans/today: booking 10 (A305, its one MIC-WIRELESS out) and booking 9 (A301, 2 reserved:
/// 1 out, 2 returned).
const todayJson = r'''
[
  {
    "bookingId": 10,
    "roomCode": "A305",
    "start": "2026-09-28T02:05:35.507013Z",
    "end": "2026-09-28T04:05:35.507013Z",
    "requesterName": "Dr. Nimal Fernando",
    "status": "Confirmed",
    "lines": [
      {
        "typeId": 1,
        "typeCode": "MIC-WIRELESS",
        "typeName": "Wireless microphone",
        "reserved": 1,
        "out": 1,
        "returned": 0
      }
    ]
  },
  {
    "bookingId": 9,
    "roomCode": "A301",
    "start": "2026-09-28T05:15:35.507013Z",
    "end": "2026-09-28T07:15:35.507013Z",
    "requesterName": "Dr. Nimal Fernando",
    "status": "Confirmed",
    "lines": [
      {
        "typeId": 1,
        "typeCode": "MIC-WIRELESS",
        "typeName": "Wireless microphone",
        "reserved": 2,
        "out": 1,
        "returned": 2
      }
    ]
  }
]
''';

/// GET /api/loans?bookingId=9&sort=dueAt&pageSize=100: two returned (Good; Damaged with a photo) and one open.
const bookingLoansJson = r'''
{
  "items": [
    {
      "id": 4,
      "bookingId": 9,
      "roomCode": "A301",
      "itemId": 1,
      "assetTag": "EQ-MICW-001",
      "typeCode": "MIC-WIRELESS",
      "checkedOutAt": "2026-09-28T05:05:59.638534Z",
      "checkedOutByName": "Sunil Jayasinghe",
      "dueAt": "2026-09-28T07:15:35.507013Z",
      "checkedInAt": "2026-09-28T05:06:08.526587Z",
      "checkedInByName": "Sunil Jayasinghe",
      "returnCondition": "Good",
      "damageNote": null,
      "isLateReturn": false,
      "isOverdue": false,
      "hasPhoto": false
    },
    {
      "id": 5,
      "bookingId": 9,
      "roomCode": "A301",
      "itemId": 2,
      "assetTag": "EQ-MICW-002",
      "typeCode": "MIC-WIRELESS",
      "checkedOutAt": "2026-09-28T05:05:59.704906Z",
      "checkedOutByName": "Sunil Jayasinghe",
      "dueAt": "2026-09-28T07:15:35.507013Z",
      "checkedInAt": "2026-09-28T05:06:08.56034Z",
      "checkedInByName": "Sunil Jayasinghe",
      "returnCondition": "Damaged",
      "damageNote": "Cracked grille",
      "isLateReturn": false,
      "isOverdue": false,
      "hasPhoto": true
    },
    {
      "id": 6,
      "bookingId": 9,
      "roomCode": "A301",
      "itemId": 3,
      "assetTag": "EQ-MICW-003",
      "typeCode": "MIC-WIRELESS",
      "checkedOutAt": "2026-09-28T05:06:15.913733Z",
      "checkedOutByName": "Sunil Jayasinghe",
      "dueAt": "2026-09-28T07:15:35.507013Z",
      "checkedInAt": null,
      "checkedInByName": null,
      "returnCondition": null,
      "damageNote": null,
      "isLateReturn": false,
      "isOverdue": false,
      "hasPhoto": false
    }
  ],
  "page": 1,
  "pageSize": 100,
  "total": 3
}
''';

/// GET /api/loans?overdue=true&sort=dueAt&pageSize=100: EQ-MICW-007, due 09:35 campus time.
const overdueJson = r'''
{
  "items": [
    {
      "id": 3,
      "bookingId": 10,
      "roomCode": "A305",
      "itemId": 7,
      "assetTag": "EQ-MICW-007",
      "typeCode": "MIC-WIRELESS",
      "checkedOutAt": "2026-09-28T02:05:35.507013Z",
      "checkedOutByName": "Sunil Jayasinghe",
      "dueAt": "2026-09-28T04:05:35.507013Z",
      "checkedInAt": null,
      "checkedInByName": null,
      "returnCondition": null,
      "damageNote": null,
      "isLateReturn": false,
      "isOverdue": true,
      "hasPhoto": false
    }
  ],
  "page": 1,
  "pageSize": 100,
  "total": 1
}
''';

/// GET /api/equipment-items?typeId=1&status=Available&sort=assetTag&pageSize=100.
const availableItemsJson = r'''
{
  "items": [
    {
      "id": 1,
      "assetTag": "EQ-MICW-001",
      "typeId": 1,
      "typeCode": "MIC-WIRELESS",
      "typeName": "Wireless microphone",
      "condition": "Good",
      "status": "Available",
      "notes": null,
      "updatedAt": "2026-09-28T05:06:08.526685Z"
    },
    {
      "id": 4,
      "assetTag": "EQ-MICW-004",
      "typeId": 1,
      "typeCode": "MIC-WIRELESS",
      "typeName": "Wireless microphone",
      "condition": "Good",
      "status": "Available",
      "notes": null,
      "updatedAt": "2026-09-27T13:02:47.597577Z"
    },
    {
      "id": 5,
      "assetTag": "EQ-MICW-005",
      "typeId": 1,
      "typeCode": "MIC-WIRELESS",
      "typeName": "Wireless microphone",
      "condition": "Good",
      "status": "Available",
      "notes": null,
      "updatedAt": "2026-09-27T13:02:47.597577Z"
    },
    {
      "id": 6,
      "assetTag": "EQ-MICW-006",
      "typeId": 1,
      "typeCode": "MIC-WIRELESS",
      "typeName": "Wireless microphone",
      "condition": "Good",
      "status": "Available",
      "notes": null,
      "updatedAt": "2026-09-27T13:02:47.597577Z"
    }
  ],
  "page": 1,
  "pageSize": 100,
  "total": 4
}
''';

/// POST /api/loans/checkout {bookingId: 9, itemId: 3} (201): an open loan.
const openLoanJson = r'''
{
  "id": 6,
  "bookingId": 9,
  "roomCode": "A301",
  "itemId": 3,
  "assetTag": "EQ-MICW-003",
  "typeCode": "MIC-WIRELESS",
  "checkedOutAt": "2026-09-28T05:06:15.913733Z",
  "checkedOutByName": "Sunil Jayasinghe",
  "dueAt": "2026-09-28T07:15:35.507013Z",
  "checkedInAt": null,
  "checkedInByName": null,
  "returnCondition": null,
  "damageNote": null,
  "isLateReturn": false,
  "isOverdue": false,
  "hasPhoto": false
}
''';

/// POST /api/loans/5/checkin (Damaged, note and photo; 200): a returned loan.
const damagedLoanJson = r'''
{
  "id": 5,
  "bookingId": 9,
  "roomCode": "A301",
  "itemId": 2,
  "assetTag": "EQ-MICW-002",
  "typeCode": "MIC-WIRELESS",
  "checkedOutAt": "2026-09-28T05:05:59.704906Z",
  "checkedOutByName": "Sunil Jayasinghe",
  "dueAt": "2026-09-28T07:15:35.507013Z",
  "checkedInAt": "2026-09-28T05:06:08.56034Z",
  "checkedInByName": "Sunil Jayasinghe",
  "returnCondition": "Damaged",
  "damageNote": "Cracked grille",
  "isLateReturn": false,
  "isOverdue": false,
  "hasPhoto": true
}
''';

/// POST /api/loans/checkout when both reserved mics were already out (409).
const checkout409Json = r'''
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "All 2 reserved MIC-WIRELESS are already out",
  "status": 409,
  "traceId": "e37915d0bb1920d555c01f5d5a6d5b24"
}
''';

/// POST /api/loans/5/checkin with condition=Damaged and no note or photo (400).
const checkIn400Json = r'''
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "A note is required for a damaged return.",
  "status": 400,
  "errors": {
    "Note": [
      "A note is required for a damaged return."
    ],
    "Photo": [
      "A photo is required for a damaged return."
    ]
  },
  "traceId": "cdf6a7e8df040ff31fd78908a6b1e122"
}
''';

/// POST /api/loans/5/checkin again after it was checked in (409).
const checkIn409Json = r'''
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Loan is already checked in",
  "status": 409,
  "traceId": "ef263ad16de1b0c79fed60a2b70eef1b"
}
''';
