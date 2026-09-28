"""Venue Matching stub (Component A). Deterministic; calls the real room tools.

Finds free rooms with the required features, drops excluded rooms and rooms above the policy's
capacity ratio, and ranks by capacity then code (best fit first). Returns up to 3 options, where the
first is the chosen room, or "unmet" with the exact constraint that could not be met.
"""

from collections.abc import Mapping
from decimal import Decimal
from typing import Any

from langchain_core.tools import BaseTool

from app.workers.common import observe, parse_brief

MAX_OPTIONS = 3


def venue_matching(task: str, tools: Mapping[str, BaseTool]) -> dict[str, Any]:
    brief = parse_brief(task)
    attendees = int(brief["attendees"])
    required = list(brief["required_features"])
    ratio = Decimal(str(brief["max_capacity_ratio"]))
    most = ratio * attendees
    excluded = set(brief["excluded_room_ids"])

    page, error = observe(
        tools,
        "search_available_rooms",
        {
            "min_capacity": attendees,
            "features": required,
            "start_iso": brief["start"],
            "end_iso": brief["end"],
        },
    )
    if error:
        return {"options": [], "unmet": f"Room search refused the request: {error}"}

    free = [r for r in page["items"] if r["isActive"]]
    fitting = [r for r in free if r["capacity"] <= most]
    candidates = sorted(
        (r for r in fitting if r["id"] not in excluded), key=lambda r: (r["capacity"], r["code"])
    )
    wanted = ", ".join(required) or "no required features"
    if not free:
        return {
            "options": [],
            "unmet": f"No active room with at least {attendees} seats and {wanted} is free "
            f"from {brief['start']} to {brief['end']}",
        }
    if not fitting:
        return {
            "options": [],
            "unmet": f"Every free room with {wanted} seats more than {most.normalize():f} "
            f"({ratio.normalize():f} × {attendees} attendees)",
        }
    if not candidates:
        codes = ", ".join(r["code"] for r in fitting)
        return {"options": [], "unmet": f"Every free room that fits was already proposed ({codes})"}

    options = []
    for room in candidates[:MAX_OPTIONS]:
        features = [f["code"] for f in room["features"]]
        options.append(
            {
                "room_id": room["id"],
                "code": room["code"],
                "name": room["name"],
                "capacity": room["capacity"],
                "building": room["building"]["name"],
                "features": features,
                "reason": f"{room['capacity']} seats for {attendees} attendees; has {wanted}; "
                f"{room['building']['name']}",
            }
        )
    return {"options": options, "unmet": None}
