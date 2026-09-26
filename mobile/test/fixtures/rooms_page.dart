// The body of a real GET /api/rooms?page=1&pageSize=20 (as kavindi@, Development seed), captured with curl.
// Keep it as the API sends it: tests use it to check the Dart models match the backend DTOs.
const roomsPageJson = r'''
{
  "items": [
    {
      "id": 3,
      "code": "A101",
      "name": "Lecture Hall A101",
      "type": "LectureHall",
      "capacity": 120,
      "isActive": true,
      "building": {
        "id": 1,
        "code": "MB",
        "name": "Main Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "sound_system",
          "name": "Sound system"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 4,
      "code": "A102",
      "name": "Lecture Hall A102",
      "type": "LectureHall",
      "capacity": 80,
      "isActive": true,
      "building": {
        "id": 1,
        "code": "MB",
        "name": "Main Building"
      },
      "features": [
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 5,
      "code": "A201",
      "name": "Seminar Room A201",
      "type": "SeminarRoom",
      "capacity": 20,
      "isActive": true,
      "building": {
        "id": 1,
        "code": "MB",
        "name": "Main Building"
      },
      "features": [
        {
          "code": "smart_board",
          "name": "Smart board"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 6,
      "code": "A202",
      "name": "Seminar Room A202",
      "type": "SeminarRoom",
      "capacity": 25,
      "isActive": true,
      "building": {
        "id": 1,
        "code": "MB",
        "name": "Main Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 1,
      "code": "A301",
      "name": "Computer Lab A301",
      "type": "ComputerLab",
      "capacity": 48,
      "isActive": true,
      "building": {
        "id": 1,
        "code": "MB",
        "name": "Main Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "computers",
          "name": "Computers"
        },
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 2,
      "code": "A305",
      "name": "Computer Lab A305",
      "type": "ComputerLab",
      "capacity": 50,
      "isActive": true,
      "building": {
        "id": 1,
        "code": "MB",
        "name": "Main Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "computers",
          "name": "Computers"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 13,
      "code": "E101",
      "name": "Lecture Hall E101",
      "type": "LectureHall",
      "capacity": 90,
      "isActive": true,
      "building": {
        "id": 3,
        "code": "EB",
        "name": "Engineering Building"
      },
      "features": [
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 14,
      "code": "E201",
      "name": "Computer Lab E201",
      "type": "ComputerLab",
      "capacity": 40,
      "isActive": true,
      "building": {
        "id": 3,
        "code": "EB",
        "name": "Engineering Building"
      },
      "features": [
        {
          "code": "computers",
          "name": "Computers"
        },
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 15,
      "code": "EB-AUD",
      "name": "Engineering Auditorium",
      "type": "Auditorium",
      "capacity": 220,
      "isActive": true,
      "building": {
        "id": 3,
        "code": "EB",
        "name": "Engineering Building"
      },
      "features": [
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "sound_system",
          "name": "Sound system"
        }
      ]
    },
    {
      "id": 7,
      "code": "MB-AUD",
      "name": "Main Auditorium",
      "type": "Auditorium",
      "capacity": 300,
      "isActive": true,
      "building": {
        "id": 1,
        "code": "MB",
        "name": "Main Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "sound_system",
          "name": "Sound system"
        }
      ]
    },
    {
      "id": 9,
      "code": "N101",
      "name": "Lecture Hall N101",
      "type": "LectureHall",
      "capacity": 150,
      "isActive": true,
      "building": {
        "id": 2,
        "code": "NB",
        "name": "New Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "sound_system",
          "name": "Sound system"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 10,
      "code": "N102",
      "name": "Lecture Hall N102",
      "type": "LectureHall",
      "capacity": 100,
      "isActive": true,
      "building": {
        "id": 2,
        "code": "NB",
        "name": "New Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 8,
      "code": "N201",
      "name": "Computer Lab N201",
      "type": "ComputerLab",
      "capacity": 60,
      "isActive": true,
      "building": {
        "id": 2,
        "code": "NB",
        "name": "New Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "computers",
          "name": "Computers"
        },
        {
          "code": "projector",
          "name": "Projector"
        },
        {
          "code": "smart_board",
          "name": "Smart board"
        }
      ]
    },
    {
      "id": 11,
      "code": "N301",
      "name": "Seminar Room N301",
      "type": "SeminarRoom",
      "capacity": 30,
      "isActive": true,
      "building": {
        "id": 2,
        "code": "NB",
        "name": "New Building"
      },
      "features": [
        {
          "code": "ac",
          "name": "Air conditioning"
        },
        {
          "code": "smart_board",
          "name": "Smart board"
        }
      ]
    },
    {
      "id": 12,
      "code": "N302",
      "name": "Seminar Room N302",
      "type": "SeminarRoom",
      "capacity": 15,
      "isActive": true,
      "building": {
        "id": 2,
        "code": "NB",
        "name": "New Building"
      },
      "features": [
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    },
    {
      "id": 18,
      "code": "SR9",
      "name": "Test Room",
      "type": "SeminarRoom",
      "capacity": 20,
      "isActive": true,
      "building": {
        "id": 1,
        "code": "MB",
        "name": "Main Building"
      },
      "features": [
        {
          "code": "whiteboard",
          "name": "Whiteboard"
        }
      ]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 16
}
''';
