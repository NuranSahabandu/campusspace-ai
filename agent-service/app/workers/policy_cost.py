"""Policy and Cost stub (Component D). Deterministic; prices through the real quote tool.

Prices the chosen room and the priced lines (room_builtin lines are left out), collects policy facts
from the snapshot values in the brief (addendum Open question 2, option b), and writes a templated
officer summary. Validation, not this worker, decides whether anything is allowed.
"""

from collections.abc import Mapping
from decimal import Decimal
from typing import Any

from langchain_core.tools import BaseTool

from app.schemas import CENT
from app.workers.common import observe, parse_brief


def lkr(amount: Decimal) -> str:
    return f"LKR {amount.quantize(CENT):,}"


def policy_cost(task: str, tools: Mapping[str, BaseTool]) -> dict[str, Any]:
    brief = parse_brief(task)
    if brief["room_id"] is None:
        unmet = brief["venue_unmet"] or "no room was proposed"
        return {
            "quote": None,
            "policy_flags": ["No room to price"],
            "officer_summary": f"No proposal: {unmet}.",
            "unmet": unmet,
        }

    data, error = observe(
        tools,
        "calculate_quote",
        {
            "room_id": brief["room_id"],
            "start_iso": brief["start"],
            "end_iso": brief["end"],
            "requester_role": brief["requester_role"],
            "equipment": brief["priced_lines"],
        },
    )
    if error:
        return {
            "quote": None,
            "policy_flags": ["Quote refused"],
            "officer_summary": f"The quote could not be calculated: {error}.",
            "unmet": error,
        }

    quote = {
        "lines": [
            {
                "kind": ln["kind"],
                "description": ln["description"],
                "qty": ln["qty"],
                "unit_price": ln["unitPrice"],
                "line_total": ln["lineTotal"],
            }
            for ln in data["lines"]
        ],
        "subtotal": data["subtotal"],
        "discount": data["discount"],
        "discount_reason": data["discountReason"],
        "exempt": data["exempt"],
        "total": data["total"],
        "currency": data["currency"],
    }
    total = Decimal(str(data["total"]))
    budget = Decimal(str(brief["budget_lkr"]))
    within = total <= budget
    facts = brief["policy"]

    flags = [f"{'Within' if within else 'Over'} budget: {lkr(total)} vs {lkr(budget)}"]
    if data["exempt"]:
        flags.append(f"Exempt: {data['discountReason']}")
    hours = Decimal(str(brief["duration_hours"]))
    flags.append(f"Duration {hours.normalize():f} h (at most {facts['max_duration_hours']} h)")
    flags += [
        f"{code} provided by room {brief['room_code']} (not priced)" for code in brief["builtin"]
    ]
    flags += [
        f"{s['substitute_code']} ×{s['qty']} substitutes {s['requested_code']}"
        for s in brief["substitutions"]
    ]
    flags += [f"Equipment unmet: {u}" for u in brief["equipment_unmet"]]

    features = ", ".join(brief["required_features"]) or "the room's standard features"
    equipment = [f"{ln['quantity']} × {ln['code']}" for ln in brief["priced_lines"]]
    equipment += [f"{code} provided by the room" for code in brief["builtin"]]
    seats = f"({brief['room_capacity']} seats)"
    summary = (
        f"{brief['room_code']} fits {brief['attendees']} attendees {seats} "
        f"with {features}. Equipment: {'; '.join(equipment) or 'none'}. "
        f"Total {lkr(total)} (budget {lkr(budget)}): {'within' if within else 'over'} budget."
    )
    return {"quote": quote, "policy_flags": flags, "officer_summary": summary, "unmet": None}
