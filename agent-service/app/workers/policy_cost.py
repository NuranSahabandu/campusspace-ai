"""Policy and Cost stub (Component D). Deterministic; prices through the real quote tool.

Prices the chosen room and the priced lines (room_builtin lines are left out), collects policy facts
from the snapshot values in the brief (addendum Open question 2, option b), and writes a templated
officer summary. Validation, not this worker, decides whether anything is allowed.

The helpers below are pure and shared with the LLM worker (app/workers/policy_llm.py): the quote
arguments it must use, the quote rebuilt from the tool data, and the templates it falls back to.
"""

from collections.abc import Mapping
from decimal import Decimal
from typing import Any

from langchain_core.tools import BaseTool

from app.schemas import CENT
from app.workers.common import observe, parse_brief


def lkr(amount: Decimal) -> str:
    return f"LKR {amount.quantize(CENT):,}"


def quote_args(brief: Mapping[str, Any]) -> dict[str, Any]:
    """The one calculate_quote call the brief allows: its room, window, role and priced lines."""
    return {
        "room_id": brief["room_id"],
        "start_iso": brief["start"],
        "end_iso": brief["end"],
        "requester_role": brief["requester_role"],
        "equipment": brief["priced_lines"],
    }


def quote_from_tool(data: Mapping[str, Any]) -> dict[str, Any]:
    """The PolicyResult quote, built only from the calculate_quote result."""
    return {
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


def no_room_result(brief: Mapping[str, Any]) -> dict[str, Any]:
    unmet = brief["venue_unmet"] or "no room was proposed"
    return {
        "quote": None,
        "policy_flags": ["No room to price"],
        "officer_summary": f"No proposal: {unmet}.",
        "unmet": unmet,
    }


def refused_summary(error: str) -> str:
    return f"The quote could not be calculated: {error}."


def refused_result(error: str) -> dict[str, Any]:
    return {
        "quote": None,
        "policy_flags": ["Quote refused"],
        "officer_summary": refused_summary(error),
        "unmet": error,
    }


def _totals(brief: Mapping[str, Any], data: Mapping[str, Any]) -> tuple[Decimal, Decimal, bool]:
    total = Decimal(str(data["total"]))
    budget = Decimal(str(brief["budget_lkr"]))
    return total, budget, total <= budget


def template_flags(brief: Mapping[str, Any], data: Mapping[str, Any]) -> list[str]:
    total, budget, within = _totals(brief, data)
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
    return flags


def template_summary(brief: Mapping[str, Any], data: Mapping[str, Any]) -> str:
    total, budget, within = _totals(brief, data)
    features = ", ".join(brief["required_features"]) or "the room's standard features"
    equipment = [f"{ln['quantity']} × {ln['code']}" for ln in brief["priced_lines"]]
    equipment += [f"{code} provided by the room" for code in brief["builtin"]]
    seats = f"({brief['room_capacity']} seats)"
    return (
        f"{brief['room_code']} fits {brief['attendees']} attendees {seats} "
        f"with {features}. Equipment: {'; '.join(equipment) or 'none'}. "
        f"Total {lkr(total)} (budget {lkr(budget)}): {'within' if within else 'over'} budget."
    )


def policy_cost(task: str, tools: Mapping[str, BaseTool]) -> dict[str, Any]:
    brief = parse_brief(task)
    if brief["room_id"] is None:
        return no_room_result(brief)

    data, error = observe(tools, "calculate_quote", quote_args(brief))
    if error:
        return refused_result(error)
    return {
        "quote": quote_from_tool(data),
        "policy_flags": template_flags(brief, data),
        "officer_summary": template_summary(brief, data),
        "unmet": None,
    }
