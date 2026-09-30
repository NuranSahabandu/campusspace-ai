"""Supervisor / Request Planner (Component C): plan once, follow in code (plan §10.5).

load_context() reads the policy snapshot (addendum A.2), the request and the catalogs at run start.
An officer revise takes a fresh snapshot with load_policy() (see CLAUDE.md); approve, reject and
cancel never re-fetch it.
Raw requester notes are replaced by their wrapped form here, before anything reaches state.
stub_planner() is the stub planner and the LLM planner's fallback (app/workers/planner.py);
enforce_plan_rules() applies to whatever a planner returns. build_brief() writes each worker's task:
ids and values only, and the venue brief carries the plan's soft_preferences. No worker ever gets
the requester's notes or the officer's raw revise notes.
"""

import json
from collections.abc import Mapping
from datetime import datetime
from decimal import Decimal
from typing import Any

from langchain_core.tools import BaseTool

from app.guardrails import strip_tags, wrap_notes
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
MAX_TASK_CHARS = 500
OFFICER_REVISION = "Officer revision: "  # human_gate's replan_reason prefix for a revise
MAX_SOFT_PREFERENCES = 5
MAX_PREFERENCE_CHARS = 200
POLICY_FACTS = (
    "max_duration_hours",
    "max_capacity_ratio",
    "min_lead_time_hours",
    "max_advance_days_student",
    "max_advance_days_lecturer",
    "max_open_requests",
    "free_cancellation_hours",
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


def enforce_plan_rules(
    plan: Plan,
    request: Mapping[str, Any],
    catalogs: Mapping[str, Any],
    *,
    fallback: bool = False,
) -> tuple[Plan, list[str]]:
    """Code, not the planner, decides the shape: venue first, equipment only when the form asked for
    equipment, policy_cost last, each exactly once. Features and equipment come from the form,
    filtered by the catalogs. Returns the plan and every correction made to the planner's output."""
    corrections: list[str] = []
    known_features, known_equipment = set(catalogs["features"]), set(catalogs["equipment"])

    form_features = list(request["required_features"])
    for code in form_features:
        if code not in known_features:
            corrections.append(f"feature {code}: not in the feature catalog, dropped")
        elif code not in plan.required_features:
            corrections.append(f"feature {code}: on the request form, restored")
    for code in plan.required_features:
        if code not in form_features:
            corrections.append(f"feature {code}: not on the request form, dropped")

    form_lines = {e["code"]: e["quantity"] for e in request["equipment"]}
    planned = {e.type_code: e.quantity for e in plan.equipment}
    for code, qty in form_lines.items():
        if code not in known_equipment:
            corrections.append(f"equipment {code}: not in the equipment catalog, dropped")
        elif code not in planned:
            corrections.append(f"equipment {code}: on the request form, restored")
        elif planned[code] != qty:
            corrections.append(f"equipment {code}: quantity {planned[code]} replaced by {qty}")
    for code in planned:
        if code not in form_lines:
            corrections.append(f"equipment {code}: not on the request form, dropped")

    wanted = ["venue_matching"]
    if request["equipment"]:
        wanted.append("equipment_allocation")
    wanted.append("policy_cost")
    got = [s.agent for s in plan.steps]
    if got != wanted:
        corrections.append(f"steps {got} replaced by {wanted}")
    tasks: dict[str, str] = {}
    for s in plan.steps:
        tasks.setdefault(s.agent, s.task.strip())
    steps = []
    for agent in wanted:
        task = tasks.get(agent, "")
        if not 0 < len(task) <= MAX_TASK_CHARS:
            if agent in tasks:
                corrections.append(f"task for {agent}: empty or too long, default used")
            task = INSTRUCTIONS[agent]
        steps.append(PlanStep(agent=agent, task=task))

    preferences: list[str] = []
    for raw in plan.soft_preferences:
        text = " ".join(strip_tags(raw).split())[:MAX_PREFERENCE_CHARS]
        if text and text not in preferences:
            preferences.append(text)
    if len(preferences) > MAX_SOFT_PREFERENCES:
        corrections.append(f"soft_preferences: kept the first {MAX_SOFT_PREFERENCES}")

    enforced = plan.model_copy(
        update={
            "required_features": [f for f in form_features if f in known_features],
            "equipment": [
                EquipmentRequest(type_code=code, quantity=qty)
                for code, qty in form_lines.items()
                if code in known_equipment
            ],
            "soft_preferences": preferences[:MAX_SOFT_PREFERENCES],
            "steps": steps,
            "planner_fallback": fallback,  # set by code, never by the model
        }
    )
    return enforced, corrections


def build_brief(step: Mapping[str, Any], state: Mapping[str, Any]) -> str:
    """The one task string a worker receives (context isolation, plan §10.6)."""
    agent = step["agent"]
    request, policy = state["request"], state["policy"]
    window = {"start": request["start"], "end": request["end"]}
    venue = state.get("venue") or {}
    chosen = (venue.get("options") or [None])[0]

    if agent == "venue_matching":
        reason = state.get("replan_reason")
        if reason and reason.startswith(OFFICER_REVISION):
            # Officer notes reach workers only as the planner's soft_preferences, never raw.
            reason = "Officer revision (see soft_preferences)"
        brief = {
            **window,
            "attendees": request["attendees"],
            "required_features": request["required_features"],
            "excluded_room_ids": state.get("excluded_room_ids", []),
            "max_capacity_ratio": policy["max_capacity_ratio"],
            "soft_preferences": (state.get("requirements") or {}).get("soft_preferences", []),
            "replan_reason": reason,
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
