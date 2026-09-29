"""Supervisor / Request Planner (Component C): plan once, follow in code (plan §10.5).

load_context() reads the policy snapshot (addendum A.2), the request and the catalogs at run start.
An officer revise takes a fresh snapshot with load_policy() (see CLAUDE.md); approve, reject and
cancel never re-fetch it.
Raw requester notes are replaced by their wrapped form here, before anything reaches state.
stub_planner() is the Phase 4 seam: an LLM planner replaces it, and enforce_plan_rules() still
applies to whatever it returns. build_brief() writes each worker's task: ids and values only.
"""

import json
from collections.abc import Mapping
from datetime import datetime
from decimal import Decimal
from typing import Any

from langchain_core.tools import BaseTool

from app.guardrails import wrap_notes
from app.schemas import EquipmentRequest, Plan, PlanStep
from app.tools import error_text, is_error, is_unavailable, parse_json
from app.workers.common import make_task

INSTRUCTIONS = {
    "venue_matching": "Find up to 3 FREE rooms that fit the attendees and have every required "
    "feature; rank them with a one-line reason each. Skip excluded rooms.",
    "equipment_allocation": "For the chosen room, drop lines its features cover (room_builtin), "
    "check availability of the rest, and propose a substitute when a line is short.",
    "policy_cost": "Price the chosen room and priced lines with calculate_quote, list the policy "
    "facts, and write a short officer summary.",
}
ORDER = ["venue_matching", "equipment_allocation", "policy_cost"]
POLICY_FACTS = (
    "max_duration_hours",
    "max_capacity_ratio",
    "min_lead_time_hours",
    "max_advance_days_student",
    "max_advance_days_lecturer",
    "max_open_requests",
)


class SupervisorError(Exception):
    """The run cannot start: the reason is shown to the officer and the requester."""


def load_policy(tools: Mapping[str, BaseTool]) -> dict[str, Any]:
    obs = tools["get_policy"].invoke({})
    if is_error(obs):
        # Addendum A.2: no fallback to defaults, which would be hard-coded policy.
        raise SupervisorError(f"policy unavailable ({error_text(obs)})")
    return json.loads(obs)  # no money inside; the ratio is compared as Decimal(str(x))


def load_context(tools: Mapping[str, BaseTool], request_id: int) -> dict[str, Any]:
    policy = load_policy(tools)

    obs = tools["get_request_context"].invoke({"request_id": request_id})
    if is_unavailable(obs):
        detail = error_text(obs).removeprefix("unavailable: ")
        raise SupervisorError(f"Tool get_request_context unavailable: {detail}")
    if obs.startswith("TOOL_ERROR: HTTP 404"):
        raise SupervisorError("Booking request not found")
    if is_error(obs):
        raise SupervisorError(
            f"Booking request context unavailable: {error_text(obs).removeprefix('HTTP ')}"
        )
    context = parse_json(obs)

    catalogs: dict[str, Any] = {}
    for name, key in (
        ("list_feature_catalog", "features"),
        ("list_equipment_catalog", "equipment"),
    ):
        obs = tools[name].invoke({})
        if is_error(obs):
            raise SupervisorError(f"Tool {name} unavailable: {error_text(obs)}")
        catalogs[key] = [item["code"] for item in parse_json(obs)]

    club = context["club"]
    request = {
        "request_id": context["requestId"],
        "status": context["status"],
        "role": context["requesterRole"],
        "club": None
        if club is None
        else {
            "name": club["name"],
            "is_active": club["isActive"],
            "is_representative": club["requesterIsRepresentative"],
        },
        "open_request_count": context["openRequestCount"],
        "attendees": context["attendees"],
        "start": context["requestedStart"],
        "end": context["requestedEnd"],
        "required_features": list(context["requiredFeatures"]),
        "equipment": [{"code": e["code"], "quantity": e["quantity"]} for e in context["equipment"]],
        "budget_lkr": str(Decimal(str(context["budgetLkr"]))),
        # Untrusted text: only ever stored wrapped, and never used as instructions.
        "notes": wrap_notes(context.get("notes")),
    }
    return {"policy": policy, "request": request, "catalogs": catalogs}


def stub_planner(
    request: Mapping[str, Any], catalogs: Mapping[str, Any], replan_reason: str | None
) -> Plan:
    """Requirements from the FORM fields only (notes are never parsed into requirements)."""
    return Plan(
        required_features=[f for f in request["required_features"] if f in catalogs["features"]],
        equipment=[
            EquipmentRequest(type_code=e["code"], quantity=e["quantity"])
            for e in request["equipment"]
            if e["code"] in catalogs["equipment"]
        ],
        soft_preferences=[],
        steps=[PlanStep(agent=a, task=INSTRUCTIONS[a]) for a in ORDER],
    )


def enforce_plan_rules(plan: Plan, request: Mapping[str, Any]) -> Plan:
    """Code, not the planner, decides the shape: venue first, equipment only when the form asked for
    equipment, policy_cost last, each exactly once. Features and equipment come from the form."""
    wanted = ["venue_matching"]
    if request["equipment"]:
        wanted.append("equipment_allocation")
    wanted.append("policy_cost")
    tasks = {s.agent: s.task for s in plan.steps}
    return plan.model_copy(
        update={
            "required_features": list(request["required_features"]),
            "equipment": [
                EquipmentRequest(type_code=e["code"], quantity=e["quantity"])
                for e in request["equipment"]
            ],
            "steps": [PlanStep(agent=a, task=tasks.get(a) or INSTRUCTIONS[a]) for a in wanted],
        }
    )


def build_brief(step: Mapping[str, Any], state: Mapping[str, Any]) -> str:
    """The one task string a worker receives (context isolation, plan §10.6)."""
    agent = step["agent"]
    request, policy = state["request"], state["policy"]
    window = {"start": request["start"], "end": request["end"]}
    venue = state.get("venue") or {}
    chosen = (venue.get("options") or [None])[0]

    if agent == "venue_matching":
        brief = {
            **window,
            "attendees": request["attendees"],
            "required_features": request["required_features"],
            "excluded_room_ids": state.get("excluded_room_ids", []),
            "max_capacity_ratio": policy["max_capacity_ratio"],
            "soft_preferences": [],
            "replan_reason": state.get("replan_reason"),
        }
    elif agent == "equipment_allocation":
        brief = {
            **window,
            "room_id": chosen["room_id"] if chosen else None,
            "room_features": chosen["features"] if chosen else [],
            "lines": request["equipment"],
        }
    else:
        equipment = state.get("equipment") or {}
        lines = equipment.get("lines", [])
        start, end = (
            datetime.fromisoformat(request["start"]),
            datetime.fromisoformat(request["end"]),
        )
        brief = {
            **window,
            "room_id": chosen["room_id"] if chosen else None,
            "room_code": chosen["code"] if chosen else None,
            "room_capacity": chosen["capacity"] if chosen else None,
            "venue_unmet": venue.get("unmet"),
            "requester_role": request["role"],
            "attendees": request["attendees"],
            "required_features": request["required_features"],
            "budget_lkr": request["budget_lkr"],
            "duration_hours": str(round(Decimal((end - start).total_seconds()) / 3600, 2)),
            "priced_lines": [
                {"code": ln["type_code"], "quantity": ln["qty"]}
                for ln in lines
                if ln["source"] != "room_builtin"
            ],
            "builtin": [ln["type_code"] for ln in lines if ln["source"] == "room_builtin"],
            "substitutions": equipment.get("substitutions", []),
            "equipment_unmet": equipment.get("unmet", []),
            "policy": {k: policy[k] for k in POLICY_FACTS},
        }
    return make_task(step["task"], brief)
