"""Equipment Allocation stub (Component B). Deterministic; calls the real equipment tools.

For the chosen room (id and feature codes from the brief):
- a line whose type is covered by one of the room's features becomes qty 0, source "room_builtin"
  (addendum B: unpriced and not counted by V08);
- otherwise it is "portable" if enough items are available;
- otherwise a directional substitute with enough items is proposed ("substitute");
- otherwise the line is reported as unmet.
"""

from collections.abc import Mapping
from typing import Any

from langchain_core.tools import BaseTool

from app.workers.common import observe, parse_brief


def equipment_allocation(task: str, tools: Mapping[str, BaseTool]) -> dict[str, Any]:
    brief = parse_brief(task)
    requested = brief["lines"]
    room_features = set(brief["room_features"])
    window = {"start_iso": brief["start"], "end_iso": brief["end"]}
    result: dict[str, Any] = {"lines": [], "substitutions": [], "unmet": []}
    if not requested:
        return result

    rows, error = observe(
        tools,
        "check_equipment_availability",
        {"codes": [line["code"] for line in requested], **window},
    )
    if error:
        result["unmet"].append(f"Availability check refused the request: {error}")
        return result
    by_code = {row["code"]: row for row in rows}

    for line in requested:
        code, qty = line["code"], int(line["quantity"])
        row = by_code[code]
        covered = row["coveredByFeatureCode"]
        if covered and covered in room_features:
            result["lines"].append({"type_code": code, "qty": 0, "source": "room_builtin"})
        elif row["available"] >= qty:
            result["lines"].append({"type_code": code, "qty": qty, "source": "portable"})
        else:
            substitute = _substitute(tools, code, qty, window)
            if substitute is None:
                result["unmet"].append(
                    f"{code}: {qty} requested, {row['available']} available, "
                    f"and no substitute has {qty} available"
                )
                continue
            sub_code, sub_available = substitute
            result["lines"].append({"type_code": sub_code, "qty": qty, "source": "substitute"})
            result["substitutions"].append(
                {
                    "requested_code": code,
                    "substitute_code": sub_code,
                    "qty": qty,
                    "reason": f"only {row['available']} {code} available; "
                    f"{sub_available} {sub_code} available",
                }
            )
    return result


def _substitute(
    tools: Mapping[str, BaseTool], code: str, qty: int, window: dict[str, str]
) -> tuple[str, int] | None:
    substitutes, error = observe(tools, "get_substitutes", {"code": code})
    if error or not substitutes:
        return None
    rows, error = observe(
        tools,
        "check_equipment_availability",
        {"codes": [s["code"] for s in substitutes], **window},
    )
    if error:
        return None
    for row in rows:  # the order .NET returns them in
        if row["available"] >= qty:
            return row["code"], row["available"]
    return None
