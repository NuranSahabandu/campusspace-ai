// Bodies captured with curl from the running API (Development seed) on 2026-09-27. Keep them as the API sends
// them: tests use them to check the Dart models match the backend DTOs.

/// GET /api/booking-requests/eligibility as kavindi@ (Robotics Club representative).
const eligibilityStudentJson = r'''
{
  "canSubmit": true,
  "reason": null,
  "clubs": [
    {
      "id": 1,
      "name": "Robotics Club"
    }
  ],
  "openRequests": 1,
  "maxOpenRequests": 3,
  "clubRequired": true
}
''';

/// GET /api/booking-requests/eligibility as ishan@ (a club member, not a representative).
const eligibilityNotRepJson = r'''
{
  "canSubmit": false,
  "reason": "You must be the registered representative of an active club",
  "clubs": [],
  "openRequests": 0,
  "maxOpenRequests": 3,
  "clubRequired": true
}
''';

/// GET /api/booking-requests/eligibility as lecturer@.
const eligibilityLecturerJson = r'''
{
  "canSubmit": true,
  "reason": null,
  "clubs": [],
  "openRequests": 1,
  "maxOpenRequests": 3,
  "clubRequired": false
}
''';

/// GET /api/policy-settings/public.
const policyJson = r'''
{
  "opening_hours": {
    "mon": {
      "open": "08:00",
      "close": "20:00"
    },
    "tue": {
      "open": "08:00",
      "close": "20:00"
    },
    "wed": {
      "open": "08:00",
      "close": "20:00"
    },
    "thu": {
      "open": "08:00",
      "close": "20:00"
    },
    "fri": {
      "open": "08:00",
      "close": "20:00"
    },
    "sat": {
      "open": "08:00",
      "close": "16:00"
    },
    "sun": null
  },
  "min_lead_time_hours": 48,
  "max_advance_days_student": 60,
  "max_advance_days_lecturer": 90,
  "max_duration_hours": 8,
  "max_capacity_ratio": 3,
  "slot_granularity_minutes": 30,
  "free_cancellation_hours": 24,
  "max_open_requests": 3
}
''';

/// GET /api/equipment-types?pageSize=100&sort=code.
const equipmentTypesJson = r'''
{
  "items": [
    {
      "id": 9,
      "code": "CAMERA-VIDEO",
      "name": "Video camera",
      "category": "Visual",
      "feePerBooking": 1500.00,
      "coveredByFeatureCode": null,
      "coveredByFeatureName": null,
      "itemCounts": {
        "total": 3,
        "available": 3,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 0
      }
    },
    {
      "id": 7,
      "code": "CLICKER",
      "name": "Presentation clicker",
      "category": "Presentation",
      "feePerBooking": 100.00,
      "coveredByFeatureCode": null,
      "coveredByFeatureName": null,
      "itemCounts": {
        "total": 8,
        "available": 8,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 0
      }
    },
    {
      "id": 10,
      "code": "EXT-CABLE",
      "name": "Extension cable",
      "category": "Accessory",
      "feePerBooking": 0.00,
      "coveredByFeatureCode": null,
      "coveredByFeatureName": null,
      "itemCounts": {
        "total": 8,
        "available": 8,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 0
      }
    },
    {
      "id": 8,
      "code": "LAPTOP",
      "name": "Laptop",
      "category": "Computing",
      "feePerBooking": 750.00,
      "coveredByFeatureCode": null,
      "coveredByFeatureName": null,
      "itemCounts": {
        "total": 8,
        "available": 7,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 1
      }
    },
    {
      "id": 2,
      "code": "MIC-WIRED",
      "name": "Wired microphone",
      "category": "Audio",
      "feePerBooking": 200.00,
      "coveredByFeatureCode": null,
      "coveredByFeatureName": null,
      "itemCounts": {
        "total": 8,
        "available": 8,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 0
      }
    },
    {
      "id": 1,
      "code": "MIC-WIRELESS",
      "name": "Wireless microphone",
      "category": "Audio",
      "feePerBooking": 500.00,
      "coveredByFeatureCode": null,
      "coveredByFeatureName": null,
      "itemCounts": {
        "total": 8,
        "available": 7,
        "onLoan": 0,
        "underRepair": 1,
        "retired": 0
      }
    },
    {
      "id": 4,
      "code": "PROJ-PORTABLE",
      "name": "Portable projector",
      "category": "Visual",
      "feePerBooking": 1500.00,
      "coveredByFeatureCode": "projector",
      "coveredByFeatureName": "Projector",
      "itemCounts": {
        "total": 5,
        "available": 5,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 0
      }
    },
    {
      "id": 5,
      "code": "SCREEN-PORTABLE",
      "name": "Portable projection screen",
      "category": "Visual",
      "feePerBooking": 500.00,
      "coveredByFeatureCode": null,
      "coveredByFeatureName": null,
      "itemCounts": {
        "total": 4,
        "available": 4,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 0
      }
    },
    {
      "id": 3,
      "code": "SPEAKER-PORTABLE",
      "name": "Portable speaker",
      "category": "Audio",
      "feePerBooking": 1000.00,
      "coveredByFeatureCode": "sound_system",
      "coveredByFeatureName": "Sound system",
      "itemCounts": {
        "total": 4,
        "available": 4,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 0
      }
    },
    {
      "id": 6,
      "code": "WHITEBOARD-MOBILE",
      "name": "Mobile whiteboard",
      "category": "Presentation",
      "feePerBooking": 300.00,
      "coveredByFeatureCode": "whiteboard",
      "coveredByFeatureName": "Whiteboard",
      "itemCounts": {
        "total": 4,
        "available": 4,
        "onLoan": 0,
        "underRepair": 0,
        "retired": 0
      }
    }
  ],
  "page": 1,
  "pageSize": 100,
  "total": 10
}
''';

/// GET /api/booking-requests?page=1&pageSize=20 as lecturer@.
const requestsPageJson = r'''
{
  "items": [
    {
      "id": 2,
      "purpose": "Guest lecture: AI in agriculture",
      "status": "Submitted",
      "requestedStart": "2026-10-26T04:30:00Z",
      "requestedEnd": "2026-10-26T06:30:00Z",
      "attendees": 120,
      "budgetLkr": 0.00,
      "clubName": null,
      "requesterName": "Dr. Nimal Fernando",
      "createdAt": "2026-09-27T13:02:47.677815Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 1
}
''';

/// GET /api/booking-requests/2 as lecturer@ (the same shape as the 201 body of POST).
const requestDetailJson = r'''
{
  "id": 2,
  "purpose": "Guest lecture: AI in agriculture",
  "status": "Submitted",
  "attendees": 120,
  "requestedStart": "2026-10-26T04:30:00Z",
  "requestedEnd": "2026-10-26T06:30:00Z",
  "budgetLkr": 0.00,
  "notes": null,
  "requester": {
    "id": 2,
    "name": "Dr. Nimal Fernando",
    "email": "lecturer@campusspace.local"
  },
  "club": null,
  "requiredFeatures": [
    {
      "code": "projector",
      "name": "Projector"
    },
    {
      "code": "sound_system",
      "name": "Sound system"
    }
  ],
  "equipment": [
    {
      "typeId": 1,
      "typeCode": "MIC-WIRELESS",
      "typeName": "Wireless microphone",
      "quantity": 2
    }
  ],
  "history": [
    {
      "fromStatus": null,
      "toStatus": "Submitted",
      "changedById": 2,
      "changedByName": "Dr. Nimal Fernando",
      "reason": null,
      "changedAt": "2026-09-27T13:02:47.677608Z"
    }
  ],
  "latestProposal": null,
  "createdAt": "2026-09-27T13:02:47.677815Z",
  "updatedAt": "2026-09-27T13:02:47.677815Z"
}
''';

/// GET /api/booking-requests/2 as kavindi@ (someone else's request).
const forbiddenJson = r'''
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "You can only view your own requests",
  "status": 403,
  "traceId": "821b7a5e7197d90c08278741767fc613"
}
''';

/// POST /api/booking-requests as kavindi@ with a past start, a 3-decimal budget and an unknown club.
const submit400Json = r'''
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Start must be in the future.",
  "status": 400,
  "errors": {
    "RequestedStart": [
      "Start must be in the future."
    ],
    "BudgetLkr": [
      "Budget can have at most 2 decimal places."
    ],
    "ClubId": [
      "You must be the registered representative of an active club"
    ]
  },
  "traceId": "7b16dffa8e988c8ccaa8fe010bb5149e"
}
''';
