// Captured from the Development API (seeded data) on 2026-10-01 by /tmp/checklist-5.3/capture.sh.
// Verbatim responses: do not edit by hand; recapture after a contract change.
import type { DashboardDto, DemandReportDto, UtilizationReportDto } from '../../api/types'

export const DASHBOARD: DashboardDto = {
  "today": "2026-10-01",
  "from": "2026-09-25",
  "to": "2026-10-01",
  "pendingApprovals": 4,
  "todayBookings": 0,
  "utilization": {
    "bookedHours": 0,
    "availableHours": 1288.00,
    "utilization": 0
  },
  "utilizationByBuilding": [
    {
      "buildingId": 3,
      "code": "EB",
      "name": "Engineering Building",
      "rooms": 3,
      "figures": {
        "bookedHours": 0,
        "availableHours": 204.00,
        "utilization": 0
      }
    },
    {
      "buildingId": 1,
      "code": "MB",
      "name": "Main Building",
      "rooms": 11,
      "figures": {
        "bookedHours": 0,
        "availableHours": 744.00,
        "utilization": 0
      }
    },
    {
      "buildingId": 2,
      "code": "NB",
      "name": "New Building",
      "rooms": 5,
      "figures": {
        "bookedHours": 0,
        "availableHours": 340.00,
        "utilization": 0
      }
    }
  ],
  "bookingsPerDay": [
    {
      "date": "2026-09-25",
      "count": 0
    },
    {
      "date": "2026-09-26",
      "count": 0
    },
    {
      "date": "2026-09-27",
      "count": 0
    },
    {
      "date": "2026-09-28",
      "count": 0
    },
    {
      "date": "2026-09-29",
      "count": 0
    },
    {
      "date": "2026-09-30",
      "count": 0
    },
    {
      "date": "2026-10-01",
      "count": 0
    }
  ],
  "agent": {
    "successRate": 0.8,
    "reachedGate": 20,
    "finished": 25,
    "avgProcessingMs": 2371,
    "processingRuns": 20
  }
}

/** 2026-09-02 to 2026-10-31 */
export const UTILIZATION: UtilizationReportDto = {
  "from": "2026-09-02",
  "to": "2026-10-31",
  "overall": {
    "bookedHours": 3.00,
    "availableHours": 11168.00,
    "utilization": 0.0003
  },
  "buildings": [
    {
      "buildingId": 3,
      "code": "EB",
      "name": "Engineering Building",
      "rooms": 3,
      "figures": {
        "bookedHours": 0,
        "availableHours": 1764.00,
        "utilization": 0
      }
    },
    {
      "buildingId": 1,
      "code": "MB",
      "name": "Main Building",
      "rooms": 11,
      "figures": {
        "bookedHours": 3.00,
        "availableHours": 6464.00,
        "utilization": 0.0005
      }
    },
    {
      "buildingId": 2,
      "code": "NB",
      "name": "New Building",
      "rooms": 5,
      "figures": {
        "bookedHours": 0,
        "availableHours": 2940.00,
        "utilization": 0
      }
    }
  ],
  "rooms": [
    {
      "roomId": 13,
      "code": "E101",
      "name": "Lecture Hall E101",
      "buildingId": 3,
      "buildingCode": "EB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 14,
      "code": "E201",
      "name": "Computer Lab E201",
      "buildingId": 3,
      "buildingCode": "EB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 15,
      "code": "EB-AUD",
      "name": "Engineering Auditorium",
      "buildingId": 3,
      "buildingCode": "EB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 3,
      "code": "A101",
      "name": "Lecture Hall A101",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 584.00,
        "utilization": 0
      }
    },
    {
      "roomId": 4,
      "code": "A102",
      "name": "Lecture Hall A102",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 5,
      "code": "A201",
      "name": "Seminar Room A201",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 6,
      "code": "A202",
      "name": "Seminar Room A202",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 1,
      "code": "A301",
      "name": "Computer Lab A301",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 3.00,
        "availableHours": 588.00,
        "utilization": 0.0051
      }
    },
    {
      "roomId": 2,
      "code": "A305",
      "name": "Computer Lab A305",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 7,
      "code": "MB-AUD",
      "name": "Main Auditorium",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 17,
      "code": "Z51A-1",
      "name": "5.1a Lab 50% full",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 18,
      "code": "Z51A-2",
      "name": "5.1a Lab 500 seats",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 19,
      "code": "Z51A-3",
      "name": "5.1a a_b",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 20,
      "code": "Z51A-4",
      "name": "5.1a axb",
      "buildingId": 1,
      "buildingCode": "MB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 9,
      "code": "N101",
      "name": "Lecture Hall N101",
      "buildingId": 2,
      "buildingCode": "NB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 10,
      "code": "N102",
      "name": "Lecture Hall N102",
      "buildingId": 2,
      "buildingCode": "NB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 8,
      "code": "N201",
      "name": "Computer Lab N201",
      "buildingId": 2,
      "buildingCode": "NB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 11,
      "code": "N301",
      "name": "Seminar Room N301",
      "buildingId": 2,
      "buildingCode": "NB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    },
    {
      "roomId": 12,
      "code": "N302",
      "name": "Seminar Room N302",
      "buildingId": 2,
      "buildingCode": "NB",
      "isActive": true,
      "figures": {
        "bookedHours": 0,
        "availableHours": 588.00,
        "utilization": 0
      }
    }
  ]
}

/** 2026-09-02 to 2026-10-31 */
export const DEMAND: DemandReportDto = {
  "from": "2026-09-02",
  "to": "2026-10-31",
  "total": 26,
  "byDay": [
    {
      "date": "2026-09-02",
      "count": 0
    },
    {
      "date": "2026-09-03",
      "count": 0
    },
    {
      "date": "2026-09-04",
      "count": 0
    },
    {
      "date": "2026-09-05",
      "count": 0
    },
    {
      "date": "2026-09-06",
      "count": 0
    },
    {
      "date": "2026-09-07",
      "count": 0
    },
    {
      "date": "2026-09-08",
      "count": 0
    },
    {
      "date": "2026-09-09",
      "count": 0
    },
    {
      "date": "2026-09-10",
      "count": 0
    },
    {
      "date": "2026-09-11",
      "count": 0
    },
    {
      "date": "2026-09-12",
      "count": 0
    },
    {
      "date": "2026-09-13",
      "count": 0
    },
    {
      "date": "2026-09-14",
      "count": 0
    },
    {
      "date": "2026-09-15",
      "count": 0
    },
    {
      "date": "2026-09-16",
      "count": 0
    },
    {
      "date": "2026-09-17",
      "count": 0
    },
    {
      "date": "2026-09-18",
      "count": 0
    },
    {
      "date": "2026-09-19",
      "count": 0
    },
    {
      "date": "2026-09-20",
      "count": 0
    },
    {
      "date": "2026-09-21",
      "count": 0
    },
    {
      "date": "2026-09-22",
      "count": 0
    },
    {
      "date": "2026-09-23",
      "count": 0
    },
    {
      "date": "2026-09-24",
      "count": 0
    },
    {
      "date": "2026-09-25",
      "count": 0
    },
    {
      "date": "2026-09-26",
      "count": 0
    },
    {
      "date": "2026-09-27",
      "count": 5
    },
    {
      "date": "2026-09-28",
      "count": 3
    },
    {
      "date": "2026-09-29",
      "count": 13
    },
    {
      "date": "2026-09-30",
      "count": 0
    },
    {
      "date": "2026-10-01",
      "count": 5
    },
    {
      "date": "2026-10-02",
      "count": 0
    },
    {
      "date": "2026-10-03",
      "count": 0
    },
    {
      "date": "2026-10-04",
      "count": 0
    },
    {
      "date": "2026-10-05",
      "count": 0
    },
    {
      "date": "2026-10-06",
      "count": 0
    },
    {
      "date": "2026-10-07",
      "count": 0
    },
    {
      "date": "2026-10-08",
      "count": 0
    },
    {
      "date": "2026-10-09",
      "count": 0
    },
    {
      "date": "2026-10-10",
      "count": 0
    },
    {
      "date": "2026-10-11",
      "count": 0
    },
    {
      "date": "2026-10-12",
      "count": 0
    },
    {
      "date": "2026-10-13",
      "count": 0
    },
    {
      "date": "2026-10-14",
      "count": 0
    },
    {
      "date": "2026-10-15",
      "count": 0
    },
    {
      "date": "2026-10-16",
      "count": 0
    },
    {
      "date": "2026-10-17",
      "count": 0
    },
    {
      "date": "2026-10-18",
      "count": 0
    },
    {
      "date": "2026-10-19",
      "count": 0
    },
    {
      "date": "2026-10-20",
      "count": 0
    },
    {
      "date": "2026-10-21",
      "count": 0
    },
    {
      "date": "2026-10-22",
      "count": 0
    },
    {
      "date": "2026-10-23",
      "count": 0
    },
    {
      "date": "2026-10-24",
      "count": 0
    },
    {
      "date": "2026-10-25",
      "count": 0
    },
    {
      "date": "2026-10-26",
      "count": 0
    },
    {
      "date": "2026-10-27",
      "count": 0
    },
    {
      "date": "2026-10-28",
      "count": 0
    },
    {
      "date": "2026-10-29",
      "count": 0
    },
    {
      "date": "2026-10-30",
      "count": 0
    },
    {
      "date": "2026-10-31",
      "count": 0
    }
  ],
  "byHour": [
    {
      "hour": 0,
      "count": 0
    },
    {
      "hour": 1,
      "count": 0
    },
    {
      "hour": 2,
      "count": 0
    },
    {
      "hour": 3,
      "count": 0
    },
    {
      "hour": 4,
      "count": 0
    },
    {
      "hour": 5,
      "count": 0
    },
    {
      "hour": 6,
      "count": 0
    },
    {
      "hour": 7,
      "count": 0
    },
    {
      "hour": 8,
      "count": 0
    },
    {
      "hour": 9,
      "count": 2
    },
    {
      "hour": 10,
      "count": 10
    },
    {
      "hour": 11,
      "count": 0
    },
    {
      "hour": 12,
      "count": 0
    },
    {
      "hour": 13,
      "count": 1
    },
    {
      "hour": 14,
      "count": 13
    },
    {
      "hour": 15,
      "count": 0
    },
    {
      "hour": 16,
      "count": 0
    },
    {
      "hour": 17,
      "count": 0
    },
    {
      "hour": 18,
      "count": 0
    },
    {
      "hour": 19,
      "count": 0
    },
    {
      "hour": 20,
      "count": 0
    },
    {
      "hour": 21,
      "count": 0
    },
    {
      "hour": 22,
      "count": 0
    },
    {
      "hour": 23,
      "count": 0
    }
  ],
  "approvals": {
    "approved": 4,
    "officerRejected": 6,
    "decided": 10,
    "approvalRate": 0.4,
    "closedAutomatically": 1,
    "cancelledBeforeDecision": 4,
    "agentFailed": 4,
    "revisionsRequested": 3
  }
}
