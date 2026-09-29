"""Deterministic validation, rules V01–V12 (plan §10.8). Plain code: the LLM never decides validity.

The rule functions are pure: they take the data plus `policy` (the run's snapshot, addendum A.3) and
`now`, and return (passed, message). No policy number appears in this file. Every limit comes from
the snapshot. validate_proposal() gathers current data by re-querying the read-only tools (V02,
V07, V08, V09, V11), because data can change between proposal and approval.

V05/V06 mirror BookingWindowRules.CheckSlot/CheckTiming in .NET, messages included, so the officer
sees the same wording as the API and the mobile app.
"""

from collections.abc import Mapping
from datetime import datetime, time, timedelta, timezone
from decimal import Decimal
from typing import Any

from langchain_core.tools import BaseTool
from pydantic import ValidationError

from app.schemas import CENT, RESULT_MODELS, Plan
from app.tools import error_text, is_error, parse_json

# CampusTime.Offset in .NET: campus local time is fixed at +05:30 (not a policy value).
CAMPUS = timezone(timedelta(hours=5, minutes=30))
DAYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"]
DAY_NAMES = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

RULES = [f"V{n:02d}" for n in range(1, 13)]
# Failures a new plan can fix (plan §10.8 "Re-plan"). Everything else is a safe failure.
RECOVERABLE = frozenset({"V02", "V03", "V04", "V07", "V08", "V10", "V12"})
HOLIDAYS_NOTE = "public holidays are not checked (not implemented)"

Check = tuple[bool, str]


def instant(value: str) -> datetime:
    return datetime.fromisoformat(value)


def lkr(amount: Decimal) -> str:
    return f"LKR {amount.quantize(CENT):,}"


# ---------- Pure rules ----------


def v01_schemas(plan: dict[str, Any] | None, outputs: Mapping[str, dict[str, Any] | None]) -> Check:
    problems = []
    checked = 0
    if plan is not None:
        try:
            Plan.model_validate(plan)
            checked += 1
        except ValidationError as exc:
            problems.append(f"plan: {_first_error(exc)}")
    for agent, output in outputs.items():
        if output is None:
            continue
        try:
            RESULT_MODELS[agent].model_validate(output)
            checked += 1
        except ValidationError as exc:
            problems.append(f"{agent}: {_first_error(exc)}")
    if problems:
        return False, "; ".join(problems)
    return True, f"{checked} outputs match their schemas (extra fields forbidden)"


def _first_error(exc: ValidationError) -> str:
    error = exc.errors()[0]
    return f"{'.'.join(str(p) for p in error['loc'])}: {error['msg']}"


def v02_room_free(
    code: str | None,
    room_ok: bool,
    free: bool,
    detail: str = "",
    availability_error: str | None = None,
) -> Check:
    """Says "no longer free" only when the availability query succeeded without the room."""
    if code is None:
        return False, f"No room was proposed{': ' + detail if detail else ''}"
    if not room_ok:
        return (
            False,
            f"Room {code} no longer exists or is inactive{': ' + detail if detail else ''}",
        )
    if availability_error:
        return False, f"Availability check failed: {availability_error}"
    if not free:
        return False, f"Room {code} is no longer free for the requested window"
    return True, f"Room {code} exists and is still free for the window"


def v03_capacity(capacity: int | None, attendees: int, policy: Mapping[str, Any]) -> Check:
    ratio = Decimal(str(policy["max_capacity_ratio"]))
    most = ratio * attendees
    if capacity is None:
        return False, "No room to check"
    if capacity < attendees:
        return False, f"{capacity} seats is fewer than the {attendees} attendees"
    if capacity > most:
        return False, f"{capacity} seats is more than {ratio.normalize():f} × {attendees} attendees"
    return True, f"{capacity} seats for {attendees} attendees (at most {most.normalize():f})"


def v04_features(code: str | None, room_features: list[str], required: list[str]) -> Check:
    if code is None:
        return False, "No room to check"
    missing = [f for f in required if f not in room_features]
    if missing:
        return False, f"Room {code} lacks {', '.join(missing)}"
    if not required:
        return True, "No features required"
    return True, f"Room {code} has {', '.join(required)}"


def v05_slot(start: datetime, end: datetime, policy: Mapping[str, Any]) -> Check:
    """Opening hours, granularity, same day and max duration (CheckSlot)."""
    s, e = start.astimezone(CAMPUS), end.astimezone(CAMPUS)
    days = f"{DAY_NAMES[s.weekday()]}s"
    hours = policy["opening_hours"][DAYS[s.weekday()]]
    granularity = int(policy["slot_granularity_minutes"])
    max_hours = int(policy["max_duration_hours"])
    boundary = f"Must be on a {granularity}-minute boundary"

    if hours is None:
        return False, f"Start: The campus is closed on {days}"
    open_at, close_at = time.fromisoformat(hours["open"]), time.fromisoformat(hours["close"])
    if not _on_boundary(s, granularity):
        return False, f"Start: {boundary}"
    if s.time() < open_at:
        return False, f"Start: Opens at {hours['open']} on {days}"
    if s.time() >= close_at:
        return False, f"Start: Closes at {hours['close']} on {days}"
    if not _on_boundary(e, granularity):
        return False, f"End: {boundary}"
    if e <= s:
        return False, "End: End must be after start"
    if e.date() != s.date():
        return False, "End: Must end on the same day as the start"
    if e.time() > close_at:
        return False, f"End: Must end by {hours['close']} on {days}"
    if e - s > timedelta(hours=max_hours):
        return False, f"End: Bookings can be at most {max_hours} hours"
    length = (e - s).total_seconds() / 3600
    return True, (
        f"{s:%a %H:%M}–{e:%H:%M} is within {hours['open']}–{hours['close']}, "
        f"{length:g} h ≤ {max_hours} h, on {granularity}-minute boundaries"
    )


def _on_boundary(value: datetime, minutes: int) -> bool:
    seconds = value.hour * 3600 + value.minute * 60 + value.second
    return value.microsecond == 0 and seconds % (minutes * 60) == 0


def v06_timing(start: datetime, role: str, policy: Mapping[str, Any], now: datetime) -> Check:
    """Lead time and advance window for the requester's role (CheckTiming)."""
    lead = int(policy["min_lead_time_hours"])
    key = "max_advance_days_lecturer" if role == "Lecturer" else "max_advance_days_student"
    max_days = int(policy[key])
    if start <= now:
        return False, "Start must be in the future."
    if start < now + timedelta(hours=lead):
        return False, f"Must start at least {lead} hours from now"
    today = now.astimezone(CAMPUS).date()
    if start.astimezone(CAMPUS).date() > today + timedelta(days=max_days):
        return False, f"Can be booked at most {max_days} days ahead"
    return True, f"Starts ≥ {lead} h from now and within {max_days} days ({role})"


def v07_blackouts(code: str | None, free: bool, availability_error: str | None = None) -> Check:
    if code is None:
        return False, f"No room to check; {HOLIDAYS_NOTE}"
    if availability_error:
        return False, f"Availability check failed: {availability_error}; {HOLIDAYS_NOTE}"
    if not free:
        return False, f"Room {code} overlaps a blackout or booking; {HOLIDAYS_NOTE}"
    return True, f"No blackout or booking overlaps room {code}; {HOLIDAYS_NOTE}"


def v08_equipment(
    requested: list[dict[str, Any]],
    equipment: dict[str, Any] | None,
    available: Mapping[str, int],
    covered_by: Mapping[str, str | None],
    room_features: list[str],
) -> Check:
    """Every requested line is proposed and available in full, as itself, as a substitute, or
    provided by the room (room_builtin lines are not counted, but the room must still have the
    covering feature, addendum B)."""
    if not requested:
        return True, "No equipment requested"
    lines = (equipment or {}).get("lines", [])
    substitutions = (equipment or {}).get("substitutions", [])
    problems, notes = [], []
    needed: dict[str, int] = {}
    for line in lines:
        if line["source"] != "room_builtin":
            needed[line["type_code"]] = needed.get(line["type_code"], 0) + line["qty"]
    for req in requested:
        code, qty = req["code"], req["quantity"]
        own = [ln for ln in lines if ln["type_code"] == code]
        subs = [s for s in substitutions if s["requested_code"] == code]
        if any(ln["source"] == "room_builtin" for ln in own):
            feature = covered_by.get(code)
            if feature is None or feature not in room_features:
                problems.append(f"{code}: the room does not provide it ({feature or 'no feature'})")
            else:
                notes.append(f"{code} provided by the room ({feature})")
        elif not own and not subs:
            problems.append(f"{code} ×{qty} is not in the proposal")
        elif own and sum(ln["qty"] for ln in own) < qty:
            problems.append(f"{code}: {qty} requested, {sum(ln['qty'] for ln in own)} proposed")
    for code, qty in needed.items():
        have = available.get(code, 0)
        if have < qty:
            problems.append(f"{code}: {qty} needed, {have} available")
        else:
            notes.append(f"{qty} × {code} available ({have})")
    if problems:
        return False, "; ".join(problems)
    return True, "; ".join(notes)


def v09_quote(proposed: dict[str, Any] | None, recomputed: dict[str, Any] | None) -> Check:
    """The proposal's quote is internally consistent and equals .NET's recomputation, exactly to the
    cent (Decimal only: 0.10 + 0.20 is 0.30 here, which float would not give)."""
    if proposed is None:
        return False, "No quote to check"
    if recomputed is None:
        return False, "The quote could not be recomputed"
    lines_sum = sum((Decimal(str(ln["line_total"])) for ln in proposed["lines"]), Decimal(0))
    subtotal = Decimal(str(proposed["subtotal"]))
    discount = Decimal(str(proposed["discount"]))
    total = Decimal(str(proposed["total"]))
    if lines_sum.quantize(CENT) != subtotal.quantize(CENT):
        return False, f"Lines add up to {lkr(lines_sum)}, not the subtotal {lkr(subtotal)}"
    if (subtotal - discount).quantize(CENT) != total.quantize(CENT):
        return False, f"Subtotal {lkr(subtotal)} − discount {lkr(discount)} ≠ total {lkr(total)}"
    official = Decimal(str(recomputed["total"]))
    if official.quantize(CENT) != total.quantize(CENT):
        return False, f"Quote total {lkr(total)} ≠ .NET recomputed {lkr(official)}"
    return True, f"Total {lkr(total)} equals the .NET recomputation"


def v10_budget(total: Decimal | None, budget: Decimal) -> Check:
    if total is None:
        return False, "No quote to compare with the budget"
    if total > budget:
        return False, f"{lkr(total)} is over the budget of {lkr(budget)}"
    return True, f"{lkr(total)} ≤ budget {lkr(budget)}"


def v11_eligibility(context: Mapping[str, Any], policy: Mapping[str, Any]) -> Check:
    cap = int(policy["max_open_requests"])
    club = context.get("club")
    if context["requesterRole"] == "Student":
        if club is None:
            return False, "A student booking needs a club"
        if not club["isActive"]:
            return False, "The club is not active"
        if not club["requesterIsRepresentative"]:
            return False, "The requester is not a registered club representative"
    others = int(context["openRequestCount"])
    if others >= cap:
        return False, f"The requester already has {others} other open requests (limit {cap})"
    who = "active club, registered rep" if club else context["requesterRole"]
    return True, f"{who}; {others} other open requests < {cap}"


def v12_ids_seen(
    room_ids: list[int], codes: list[str], seen_rooms: list[int], seen_codes: list[str]
) -> Check:
    unseen = [f"room {r}" for r in room_ids if r not in seen_rooms]
    unseen += [f"equipment {c}" for c in codes if c not in seen_codes]
    if unseen:
        return False, f"Not returned by any tool in this run: {', '.join(unseen)}"
    return True, f"{len(room_ids)} room ids and {len(codes)} equipment codes came from tool results"


# ---------- Re-query (validate node and finalize) ----------


def recheck_room(tools: Mapping[str, BaseTool], room_id: int, start: str, end: str) -> dict:
    """Current room record and whether it is still free (V02/V03/V04/V07 inputs).

    `availability_error` is set only when the availability query itself failed (for example a 400
    from CheckSlot after the opening hours changed), so V02/V07 don't report the room as taken.
    """
    obs = tools["get_room_details"].invoke({"room_id": room_id})
    if is_error(obs):
        return {"room": None, "free": False, "error": error_text(obs), "availability_error": None}
    room = parse_json(obs)
    obs = tools["search_available_rooms"].invoke(
        {
            "min_capacity": room["capacity"],
            "max_capacity": room["capacity"],
            "features": [],
            "start_iso": start,
            "end_iso": end,
            "building_id": room["building"]["id"],
        }
    )
    if is_error(obs):
        return {"room": room, "free": False, "error": None, "availability_error": error_text(obs)}
    free = any(r["id"] == room_id for r in parse_json(obs)["items"])
    return {"room": room, "free": free, "error": None, "availability_error": None}


def recheck_equipment(
    tools: Mapping[str, BaseTool], codes: list[str], start: str, end: str
) -> dict:
    """Current availability and covering feature per code (V08 inputs)."""
    if not codes:
        return {"available": {}, "covered_by": {}, "error": None}
    obs = tools["check_equipment_availability"].invoke(
        {"codes": codes, "start_iso": start, "end_iso": end}
    )
    if is_error(obs):
        return {"available": {}, "covered_by": {}, "error": error_text(obs)}
    rows = parse_json(obs)
    return {
        "available": {r["code"]: r["available"] for r in rows},
        "covered_by": {r["code"]: r["coveredByFeatureCode"] for r in rows},
        "error": None,
    }


def equipment_codes(request: Mapping[str, Any], equipment: Mapping[str, Any] | None) -> list[str]:
    codes = [e["code"] for e in request["equipment"]]
    for line in (equipment or {}).get("lines", []):
        codes.append(line["type_code"])
    return list(dict.fromkeys(codes))


def v08_from_recheck(
    request: Mapping[str, Any],
    equipment: dict[str, Any] | None,
    stock: dict,
    room_features: list[str],
) -> Check:
    if stock["error"]:
        return False, f"Equipment could not be re-checked: {stock['error']}"
    return v08_equipment(
        request["equipment"], equipment, stock["available"], stock["covered_by"], room_features
    )


def validate_proposal(
    state: Mapping[str, Any], tools: Mapping[str, BaseTool], now: datetime
) -> list[tuple[str, bool, str]]:
    """All twelve rules for the current proposal, in rule order."""
    request, policy = state["request"], state["policy"]
    venue = state.get("venue") or {}
    equipment = state.get("equipment")
    result = state.get("quote") or {}
    options = venue.get("options") or []
    chosen = options[0] if options else None
    start, end = request["start"], request["end"]
    code = chosen["code"] if chosen else None
    checks: dict[str, Check] = {}

    checks["V01"] = v01_schemas(
        state.get("plan_model"),
        {
            "venue_matching": state.get("venue"),
            "equipment_allocation": equipment,
            "policy_cost": state.get("quote"),
        },
    )

    room_check = {
        "room": None,
        "free": False,
        "error": venue.get("unmet"),
        "availability_error": None,
    }
    if chosen:
        room_check = recheck_room(tools, chosen["room_id"], start, end)
    room = room_check["room"]
    room_features = [f["code"] for f in room["features"]] if room else []
    checks["V02"] = v02_room_free(
        code,
        room is not None,
        room_check["free"],
        room_check["error"] or "",
        room_check["availability_error"],
    )
    checks["V03"] = v03_capacity(room["capacity"] if room else None, request["attendees"], policy)
    checks["V04"] = v04_features(code, room_features, request["required_features"])
    checks["V05"] = v05_slot(instant(start), instant(end), policy)
    checks["V06"] = v06_timing(instant(start), request["role"], policy, now)
    checks["V07"] = v07_blackouts(code, room_check["free"], room_check["availability_error"])

    stock = recheck_equipment(tools, equipment_codes(request, equipment), start, end)
    checks["V08"] = v08_from_recheck(request, equipment, stock, room_features)

    quote = result.get("quote")
    recomputed = None
    if chosen and quote:
        lines = [
            {"code": ln["type_code"], "quantity": ln["qty"]}
            for ln in (equipment or {}).get("lines", [])
            if ln["source"] != "room_builtin"
        ]
        obs = tools["calculate_quote"].invoke(
            {
                "room_id": chosen["room_id"],
                "start_iso": start,
                "end_iso": end,
                "requester_role": request["role"],
                "equipment": lines,
            }
        )
        recomputed = None if is_error(obs) else parse_json(obs)
    checks["V09"] = v09_quote(quote, recomputed)
    total = Decimal(str(quote["total"])) if quote else None
    checks["V10"] = v10_budget(total, Decimal(str(request["budget_lkr"])))

    obs = tools["get_request_context"].invoke({"request_id": request["request_id"]})
    if is_error(obs):
        checks["V11"] = (False, f"Eligibility could not be re-checked: {error_text(obs)}")
    else:
        checks["V11"] = v11_eligibility(parse_json(obs), policy)

    room_ids = [o["room_id"] for o in options]
    codes = [ln["type_code"] for ln in (equipment or {}).get("lines", [])]
    codes += [s["substitute_code"] for s in (equipment or {}).get("substitutions", [])]
    checks["V12"] = v12_ids_seen(
        room_ids,
        list(dict.fromkeys(codes)),
        state.get("seen_room_ids", []),
        state.get("seen_equipment_codes", []),
    )
    return [(rule, *checks[rule]) for rule in RULES]
