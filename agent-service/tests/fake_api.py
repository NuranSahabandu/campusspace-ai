"""A data-driven fake of the .NET /internal/agent-tools routes for httpx.MockTransport.

Shapes are copied from the 3.1 DTOs (camelCase JSON, Problem Details errors) and values from
Data/Seed.cs (A301/A305/N201, MIC-WIRELESS fee 500, ComputerLab/Student 1500/h, the 5,500 demo
quote). Tests change the data (busy rooms, stock, policy, request fields) instead of mocking calls.
"""

import copy
import json
import re
from dataclasses import dataclass, field
from datetime import UTC, datetime, timedelta, timezone
from decimal import ROUND_HALF_UP, Decimal
from typing import Any

import httpx

from tests.conftest import TEST_TOOLS_KEY

CAMPUS = timezone(timedelta(hours=5, minutes=30))
# Thursday 2026-10-01 09:00 campus time: "now" for every graph test.
NOW = datetime(2026, 10, 1, 9, 0, tzinfo=CAMPUS)
# Friday 2026-10-16 14:00–17:00 campus (the walkthrough slot): 15 days ahead, 3 hours.
START = "2026-10-16T08:30:00Z"
END = "2026-10-16T11:30:00Z"

ISO = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")
DAYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"]
DAY_NAMES = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

POLICY: dict[str, Any] = {
    "opening_hours": {
        **{d: {"open": "08:00", "close": "20:00"} for d in DAYS[:5]},
        "sat": {"open": "08:00", "close": "16:00"},
        "sun": None,
    },
    "min_lead_time_hours": 48,
    "max_advance_days_student": 60,
    "max_advance_days_lecturer": 90,
    "max_duration_hours": 8,
    "max_capacity_ratio": 3,
    "slot_granularity_minutes": 30,
    "free_cancellation_hours": 24,
    "max_open_requests": 3,
    "checkout_window_minutes": 30,
}

BUILDINGS = {
    "MB": {"id": 1, "code": "MB", "name": "Main Building"},
    "NB": {"id": 2, "code": "NB", "name": "New Building"},
    "EB": {"id": 3, "code": "EB", "name": "Engineering Building"},
}

# (id, code, name, type, capacity, building, features) as in Seed.DemoRooms.
ROOMS = [
    (1, "A301", "Computer Lab A301", "ComputerLab", 48, "MB", ["ac", "computers", "projector", "whiteboard"]),
    (2, "A305", "Computer Lab A305", "ComputerLab", 50, "MB", ["ac", "computers", "whiteboard"]),
    (3, "A101", "Lecture Hall A101", "LectureHall", 120, "MB", ["ac", "projector", "sound_system", "whiteboard"]),
    (4, "A102", "Lecture Hall A102", "LectureHall", 80, "MB", ["projector", "whiteboard"]),
    (5, "MB-AUD", "Main Auditorium", "Auditorium", 300, "MB", ["ac", "projector", "sound_system"]),
    (6, "N201", "Computer Lab N201", "ComputerLab", 60, "NB", ["ac", "computers", "projector", "smart_board"]),
    (7, "N101", "Lecture Hall N101", "LectureHall", 150, "NB", ["ac", "projector", "sound_system", "whiteboard"]),
    (9, "E101", "Lecture Hall E101", "LectureHall", 90, "EB", ["projector", "whiteboard"]),
    (10, "E201", "Computer Lab E201", "ComputerLab", 40, "EB", ["computers", "projector", "whiteboard"]),
]  # fmt: skip

FEATURES = ["ac", "computers", "projector", "smart_board", "sound_system", "whiteboard"]

# code: (name, category, fee, coveredBy, serviceable). MIC-WIRELESS has 8 items, one UnderRepair.
EQUIPMENT = {
    "CLICKER": ("Presentation clicker", "Presentation", "100", None, 8),
    "MIC-WIRED": ("Wired microphone", "Audio", "200", None, 8),
    "MIC-WIRELESS": ("Wireless microphone", "Audio", "500", None, 7),
    "PROJ-PORTABLE": ("Portable projector", "Visual", "1500", "projector", 5),
    "SPEAKER-PORTABLE": ("Portable speaker", "Audio", "1000", "sound_system", 4),
}
SUBSTITUTES = {"MIC-WIRELESS": ["MIC-WIRED"], "MIC-WIRED": ["MIC-WIRELESS"]}

PRICING = {  # (room type, role): (hourly rate, exempt)
    ("ComputerLab", "Student"): (Decimal("1500"), False),
    ("LectureHall", "Student"): (Decimal("1000"), False),
    ("Auditorium", "Student"): (Decimal("3000"), False),
    ("ComputerLab", "Lecturer"): (Decimal("0"), True),
    ("LectureHall", "Lecturer"): (Decimal("0"), True),
    ("Auditorium", "Lecturer"): (Decimal("0"), True),
}
ROOM_LABELS = {
    "ComputerLab": "Computer lab",
    "LectureHall": "Lecture hall",
    "Auditorium": "Auditorium",
}


def student_request(**changes: Any) -> dict[str, Any]:
    """Request 42: the walkthrough request (plan §11 with addendum B's portable projector)."""
    context = {
        "requestId": 42,
        "status": "AgentProcessing",
        "requesterRole": "Student",
        "club": {"name": "Robotics Club", "isActive": True, "requesterIsRepresentative": True},
        "openRequestCount": 1,
        "maxOpenRequests": 3,
        "purpose": "Robotics Club workshop",
        "attendees": 45,
        "requestedStart": START,
        "requestedEnd": END,
        "requiredFeatures": ["computers", "projector"],
        "equipment": [
            {"code": "MIC-WIRELESS", "quantity": 2},
            {"code": "PROJ-PORTABLE", "quantity": 1},
        ],
        "budgetLkr": Decimal("8000.00"),
        "notes": "Please unlock the room 15 minutes early.",
    }
    context.update(changes)
    return context


def lecturer_request(**changes: Any) -> dict[str, Any]:
    """Request 43: a lecturer's guest lecture (exempt, no club), as in Seed.DemoBookingRequests."""
    return student_request(
        requestId=43,
        requesterRole="Lecturer",
        club=None,
        purpose="Guest lecture: AI in agriculture",
        attendees=120,
        requiredFeatures=["projector", "sound_system"],
        equipment=[{"code": "MIC-WIRELESS", "quantity": 2}],
        budgetLkr=Decimal("0.00"),
        notes=None,
        **changes,
    )


def problem(status: int, title: str, errors: dict[str, list[str]] | None = None) -> httpx.Response:
    body: dict[str, Any] = {"type": "about:blank", "title": title, "status": status}
    if errors:
        body["errors"] = errors
    body["traceId"] = "00-fake-00"
    return httpx.Response(status, json=body, headers={"content-type": "application/problem+json"})


def _dumps(value: Any) -> str:
    """JSON with Decimal written as a raw number literal, like System.Text.Json writes decimal."""
    text = json.dumps(value, default=lambda d: f"__DEC__{d}")
    return re.sub(r'"__DEC__([-0-9.]+)"', r"\1", text)


def _json(value: Any, status: int = 200) -> httpx.Response:
    return httpx.Response(
        status, content=_dumps(value), headers={"content-type": "application/json"}
    )


@dataclass
class FakeCampusApi:
    policy: dict[str, Any] = field(default_factory=lambda: copy.deepcopy(POLICY))
    requests: dict[int, dict[str, Any]] = field(
        default_factory=lambda: {42: student_request(), 43: lecturer_request()}
    )
    inactive_rooms: set[str] = field(default_factory=set)
    busy: dict[str, list[tuple[str, str]]] = field(default_factory=dict)  # bookings + blackouts
    reserved: dict[str, int] = field(default_factory=dict)
    lose_feature: dict[str, str] = field(default_factory=dict)  # room code -> feature removed
    down: set[str] = field(default_factory=set)  # route names, or "*"
    status_override: dict[str, int] = field(default_factory=dict)
    quote_total_offset: Decimal = Decimal("0")
    calls: list[httpx.Request] = field(default_factory=list)

    def transport(self) -> httpx.MockTransport:
        return httpx.MockTransport(self.handle)

    def calls_to(self, route: str) -> list[httpx.Request]:
        return [c for c in self.calls if _route(c.url.path) == route]

    # ---------- dispatch ----------

    def handle(self, request: httpx.Request) -> httpx.Response:
        self.calls.append(request)
        if request.headers.get("X-Agent-Key") != TEST_TOOLS_KEY:
            return problem(401, "Unauthorized")
        route = _route(request.url.path)
        if "*" in self.down or route in self.down:
            raise httpx.ConnectError("Connection refused", request=request)
        if route in self.status_override:
            return problem(self.status_override[route], "Server error")
        tail = request.url.path.removeprefix("/internal/agent-tools/")
        params = request.url.params
        if route == "request-context":
            context = self.requests.get(int(tail.split("/")[1]))
            return _json(context) if context else problem(404, "Not Found")
        if route == "policy":
            return _json(self.policy)
        if route == "catalog/features":
            return _json([{"code": c, "name": c.replace("_", " ").title()} for c in FEATURES])
        if route == "catalog/equipment":
            return _json([self._type(code) for code in sorted(EQUIPMENT)])
        if route == "rooms/available":
            return self._available(params)
        if route == "rooms/{id}":
            room = self._room_by_id(int(tail.split("/")[1]))
            return _json(room) if room else problem(404, "Not Found")
        if route == "equipment/availability":
            return self._equipment(params)
        if route == "equipment/substitutes/{code}":
            code = tail.split("/")[2].upper()
            if code not in EQUIPMENT:
                return problem(404, "Not Found")
            return _json([{"code": s, "name": EQUIPMENT[s][0]} for s in SUBSTITUTES.get(code, [])])
        if route == "quote":
            return self._quote(json.loads(request.content))
        return problem(404, "Not Found")

    # ---------- data ----------

    def room(self, code: str) -> dict[str, Any]:
        rid, code, name, rtype, capacity, building, features = next(
            r for r in ROOMS if r[1] == code
        )
        features = [f for f in features if f != self.lose_feature.get(code)]
        return {
            "id": rid,
            "code": code,
            "name": name,
            "type": rtype,
            "capacity": capacity,
            "isActive": code not in self.inactive_rooms,
            "building": BUILDINGS[building],
            "features": [{"code": f, "name": f.replace("_", " ").title()} for f in features],
        }

    def _room_by_id(self, room_id: int) -> dict[str, Any] | None:
        code = next((r[1] for r in ROOMS if r[0] == room_id), None)
        if code is None or code in self.inactive_rooms:
            return None
        return self.room(code)

    def _type(self, code: str) -> dict[str, Any]:
        name, category, fee, covered, _ = EQUIPMENT[code]
        return {
            "code": code,
            "name": name,
            "category": category,
            "feePerBooking": Decimal(fee),
            "coveredByFeatureCode": covered,
        }

    # ---------- validation shared by the routes (IsoInstant + CheckSlot, V05 only) ----------

    def _window(self, start: str | None, end: str | None) -> tuple[Any, httpx.Response | None]:
        errors: dict[str, list[str]] = {}
        for name, value in (("Start", start), ("End", end)):
            if value is None or not ISO.match(value):
                errors[name] = [f"{name} must be ISO 8601 with an offset."]
        if errors:
            return None, problem(400, "One or more validation errors occurred.", errors)
        s = datetime.fromisoformat(start).astimezone(CAMPUS)  # type: ignore[arg-type]
        e = datetime.fromisoformat(end).astimezone(CAMPUS)  # type: ignore[arg-type]
        hours = self.policy["opening_hours"][DAYS[s.weekday()]]
        if hours is None:
            closed = f"The campus is closed on {DAY_NAMES[s.weekday()]}s"
            return None, problem(
                400, "One or more validation errors occurred.", {"Start": [closed]}
            )
        return (s, e), None

    def _overlaps(self, code: str, s: datetime, e: datetime) -> bool:
        for bs, be in self.busy.get(code, []):
            if datetime.fromisoformat(bs) < e and s < datetime.fromisoformat(be):
                return True
        return False

    def _available(self, params: httpx.QueryParams) -> httpx.Response:
        window, error = self._window(params.get("start"), params.get("end"))
        if error:
            return error
        s, e = window
        min_cap = int(params["minCapacity"])
        max_cap = int(params["maxCapacity"]) if "maxCapacity" in params else None
        wanted = [f for f in params.get("features", "").split(",") if f]
        unknown = [f for f in wanted if f not in FEATURES]
        if unknown:
            return problem(400, "Bad request", {"Features": [f"Unknown features: {unknown}"]})
        building = int(params["buildingId"]) if "buildingId" in params else None
        rooms = [self.room(r[1]) for r in ROOMS]
        items = [
            r
            for r in rooms
            if r["isActive"]
            and r["capacity"] >= min_cap
            and (max_cap is None or r["capacity"] <= max_cap)
            and set(wanted) <= {f["code"] for f in r["features"]}
            and (building is None or r["building"]["id"] == building)
            and not self._overlaps(r["code"], s, e)
        ]
        items.sort(key=lambda r: (r["capacity"], r["code"]))
        page_size = int(params.get("pageSize", 20))
        page = {"items": items[:page_size], "page": 1, "pageSize": page_size, "total": len(items)}
        return _json(page)

    def _equipment(self, params: httpx.QueryParams) -> httpx.Response:
        window, error = self._window(params.get("start"), params.get("end"))
        if error:
            return error
        codes = [c.strip().upper() for c in params["codes"].split(",") if c.strip()]
        unknown = [c for c in codes if c not in EQUIPMENT]
        if unknown:
            return problem(
                400, "Bad request", {"Codes": [f"Unknown equipment codes: {', '.join(unknown)}."]}
            )
        result = []
        for code in dict.fromkeys(codes):
            name, _, _, covered, serviceable = EQUIPMENT[code]
            reserved = self.reserved.get(code, 0)
            result.append(
                {
                    "code": code,
                    "name": name,
                    "serviceable": serviceable,
                    "reserved": reserved,
                    "available": max(serviceable - reserved, 0),
                    "overAllocated": reserved > serviceable,
                    "coveredByFeatureCode": covered,
                }
            )
        return _json(result)

    def _quote(self, body: dict[str, Any]) -> httpx.Response:
        window, error = self._window(body.get("start"), body.get("end"))
        if error:
            return error
        s, e = window
        room = self._room_by_id(body["roomId"])
        if room is None:
            return problem(404, "Not Found")
        lines_in = body.get("equipment") or []
        unknown = [line["code"] for line in lines_in if line["code"].upper() not in EQUIPMENT]
        if unknown:
            return problem(
                400,
                "Bad request",
                {"Equipment": [f"Unknown equipment codes: {', '.join(unknown)}."]},
            )
        rate, exempt = PRICING[(room["type"], body["requesterRole"])]
        hours = (Decimal((e - s).total_seconds()) / 3600).quantize(Decimal("0.01"))
        lines = [
            {
                "kind": "Room",
                "description": f"{ROOM_LABELS[room['type']]} {room['code']}, {hours.normalize():f} h @ LKR {rate:,.0f}",
                "qty": hours,
                "unitPrice": rate,
                "lineTotal": (hours * rate).quantize(Decimal("0.01"), ROUND_HALF_UP),
            }
        ]
        summed: dict[str, int] = {}
        for line in lines_in:
            if line["quantity"] > 0:  # qty 0 = room_builtin: skipped, not priced
                summed[line["code"].upper()] = (
                    summed.get(line["code"].upper(), 0) + line["quantity"]
                )
        for code, qty in summed.items():
            name, _, fee, _, _ = EQUIPMENT[code]
            lines.append(
                {
                    "kind": "Equipment",
                    "description": f"{name} x{qty} @ LKR {Decimal(fee):,.0f}",
                    "qty": Decimal(qty),
                    "unitPrice": Decimal(fee),
                    "lineTotal": (qty * Decimal(fee)).quantize(Decimal("0.01")),
                }
            )
        subtotal = sum((line["lineTotal"] for line in lines), Decimal("0.00"))
        discount = subtotal if exempt else Decimal("0.00")
        quote = {
            "id": None,
            "requestId": None,
            "status": None,
            "lines": lines,
            "subtotal": subtotal,
            "discount": discount,
            "discountReason": "Lecturer exemption (academic use)" if exempt else None,
            "exempt": exempt,
            "total": subtotal - discount + self.quote_total_offset,
            "currency": "LKR",
        }
        return _json(quote)


def _route(path: str) -> str:
    """/internal/agent-tools/rooms/12 -> 'rooms/{id}' (the names used by FakeCampusApi.down)."""
    tail = path.removeprefix("/internal/agent-tools/")
    parts = tail.split("/")
    if parts[0] == "request-context":
        return "request-context"
    if parts[0] == "rooms" and parts[1] != "available":
        return "rooms/{id}"
    if parts[:2] == ["equipment", "substitutes"]:
        return "equipment/substitutes/{code}"
    return tail


def utc(dt: datetime) -> str:
    return dt.astimezone(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")
