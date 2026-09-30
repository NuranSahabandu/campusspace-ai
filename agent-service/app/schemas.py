"""Pydantic v2 contracts: agent outputs (V01), tool arguments, trace rows and the /workflows API.

Every agent and tool model forbids extra fields, so an injected "status" or "price" field fails
validation (V01). Money is Decimal end to end and is serialized as a 2-dp string ("5500.00") so no
float ever touches a price.
"""

import re
from datetime import datetime
from decimal import Decimal
from typing import Annotated, Any, Literal
from uuid import UUID

from pydantic import (
    BaseModel,
    ConfigDict,
    Field,
    PlainSerializer,
    field_validator,
    model_validator,
)

from app.limits import MAX_NOTES_LENGTH

CENT = Decimal("0.01")

# 2-dp string in JSON (exact); a Decimal in Python.
Money = Annotated[
    Decimal, PlainSerializer(lambda d: str(d.quantize(CENT)), return_type=str, when_used="json")
]

# Same rule as the .NET IsoInstant: ISO 8601 with an explicit offset ("Z" or "+05:30").
ISO_INSTANT = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")


def _check_instant(value: str) -> str:
    if not ISO_INSTANT.match(value):
        raise ValueError("must be ISO 8601 with an offset, for example 2026-10-06T14:00:00+05:30")
    datetime.fromisoformat(value)  # rejects impossible dates such as month 13
    return value


class Strict(BaseModel):
    model_config = ConfigDict(extra="forbid")


# ---------- Supervisor ----------

AgentName = Literal["venue_matching", "equipment_allocation", "policy_cost"]


class PlanStep(Strict):
    agent: AgentName  # cannot invent a worker
    task: str = Field(min_length=1, description="One crisp instruction. IDs, not whole records.")


class EquipmentRequest(Strict):
    type_code: str = Field(min_length=1, max_length=40)
    quantity: int = Field(gt=0)


class Plan(Strict):
    required_features: list[str]  # codes from list_feature_catalog only
    equipment: list[EquipmentRequest]  # codes from list_equipment_catalog only
    soft_preferences: list[str] = []
    steps: list[PlanStep] = Field(min_length=1)
    planner_fallback: bool = False


# ---------- Venue Matching ----------


class VenueOption(Strict):
    room_id: int = Field(gt=0)
    code: str
    name: str
    capacity: int = Field(gt=0)
    building: str
    features: list[str]
    reason: str


class VenueResult(Strict):
    options: list[VenueOption] = Field(max_length=3)  # options[0] is the chosen room
    unmet: str | None = None


# ---------- Equipment Allocation ----------


class EquipmentLine(Strict):
    type_code: str
    qty: int = Field(ge=0)
    source: Literal["portable", "room_builtin", "substitute"]

    @model_validator(mode="after")
    def _builtin_is_qty_zero(self) -> "EquipmentLine":
        # Addendum B / V01: room_builtin only with qty 0, and every other line needs qty > 0.
        if (self.source == "room_builtin") != (self.qty == 0):
            raise ValueError("source 'room_builtin' requires qty 0, and only it may have qty 0")
        return self


class Substitution(Strict):
    requested_code: str
    substitute_code: str
    qty: int = Field(gt=0)
    reason: str


class EquipmentResult(Strict):
    lines: list[EquipmentLine]
    substitutions: list[Substitution] = []
    unmet: list[str] = []


# ---------- Policy and Cost ----------


class QuoteLine(Strict):
    kind: str
    description: str
    qty: Money
    unit_price: Money
    line_total: Money


class Quote(Strict):
    lines: list[QuoteLine]
    subtotal: Money
    discount: Money
    discount_reason: str | None = None
    exempt: bool
    total: Money
    currency: str = "LKR"


class PolicyResult(Strict):
    quote: Quote | None
    policy_flags: list[str]
    officer_summary: str
    unmet: str | None = None


class PolicyAnswer(Strict):
    """What the Policy and Cost MODEL writes. It cannot write a price: code composes the
    PolicyResult with the quote rebuilt from its calculate_quote result (app/workers/policy_llm.py).
    No length limits here: code trims (a retry for a long flag would waste the budget)."""

    policy_flags: list[str]
    officer_summary: str


RESULT_MODELS: dict[str, type[Strict]] = {
    "venue_matching": VenueResult,
    "equipment_allocation": EquipmentResult,
    "policy_cost": PolicyResult,
}


# ---------- Tool arguments (validated before any call to .NET) ----------


class NoArgs(Strict):
    pass


class RequestContextArgs(Strict):
    request_id: int = Field(gt=0)


class RoomSearchArgs(Strict):
    min_capacity: int = Field(gt=0)
    features: list[str] = []
    start: str
    end: str
    max_capacity: int | None = Field(default=None, gt=0)
    building_id: int | None = Field(default=None, gt=0)
    page_size: int = Field(default=100, ge=1, le=100)

    _start = field_validator("start", "end")(_check_instant)

    def as_query(self) -> dict[str, Any]:
        query: dict[str, Any] = {
            "start": self.start,
            "end": self.end,
            "minCapacity": self.min_capacity,
            "page": 1,
            "pageSize": self.page_size,
        }
        if self.features:
            query["features"] = ",".join(self.features)
        if self.max_capacity is not None:
            query["maxCapacity"] = self.max_capacity
        if self.building_id is not None:
            query["buildingId"] = self.building_id
        return query


class RoomDetailsArgs(Strict):
    room_id: int = Field(gt=0)


class EquipmentAvailabilityArgs(Strict):
    codes: list[str] = Field(min_length=1, max_length=50)
    start: str
    end: str

    _start = field_validator("start", "end")(_check_instant)

    def as_query(self) -> dict[str, Any]:
        return {"codes": ",".join(self.codes), "start": self.start, "end": self.end}


class SubstitutesArgs(Strict):
    code: str = Field(min_length=1, max_length=40, pattern=r"^[A-Za-z0-9_-]+$")


class QuoteLineArgs(Strict):
    code: str = Field(min_length=1, max_length=40)
    quantity: int = Field(ge=0, le=1000)


class QuoteArgs(Strict):
    room_id: int = Field(gt=0)
    start: str
    end: str
    requester_role: Literal["Student", "Lecturer"]
    equipment: list[QuoteLineArgs] = []

    _start = field_validator("start", "end")(_check_instant)

    def as_body(self) -> dict[str, Any]:
        return {
            "roomId": self.room_id,
            "start": self.start,
            "end": self.end,
            "requesterRole": self.requester_role,
            "equipment": [{"code": e.code, "quantity": e.quantity} for e in self.equipment],
        }


# ---------- Trace (maps 1:1 onto AgentSteps / AgentToolCalls / ValidationResults) ----------

StepStatus = Literal["Succeeded", "Failed"]  # AgentStepStatuses.All
RULE_CODE = r"^V(0[1-9]|1[0-2])$"  # CK_ValidationResults_RuleCode


class ToolCallTrace(Strict):
    tool_name: str = Field(min_length=1, max_length=50)
    args: dict[str, Any]
    result_summary: dict[str, Any] | None  # jsonb; a summary, never the raw response
    succeeded: bool
    error: str | None = None
    duration_ms: int = Field(ge=0)

    @model_validator(mode="after")
    def _error_when_failed(self) -> "ToolCallTrace":
        if not self.succeeded and not self.error:  # CK_AgentToolCalls_Error
            raise ValueError("a failed tool call needs an error")
        return self


class StepTrace(Strict):
    sequence: int = Field(ge=1)
    agent_name: str = Field(min_length=1, max_length=50)
    status: StepStatus
    input: dict[str, Any] | None
    output: dict[str, Any] | None
    retries: int = Field(default=0, ge=0)
    error: str | None = None
    duration_ms: int = Field(ge=0)
    tool_calls: list[ToolCallTrace] = []


class RuleResult(Strict):
    attempt: int = Field(gt=0)
    rule: str = Field(pattern=RULE_CODE)
    passed: bool
    message: str


# ---------- /workflows API (the contract 3.3's AgentClient codes against) ----------

WorkflowStatus = Literal[
    "running", "awaiting_approval", "completed", "rejected", "failed", "cancelled"
]
Decision = Literal["approve", "reject", "revise", "cancel"]


class StartWorkflowRequest(Strict):
    thread_id: UUID  # AgentRuns.Id, generated by .NET
    request_id: int = Field(gt=0)


class WorkflowAccepted(BaseModel):
    thread_id: str
    status: WorkflowStatus


class ResumeRequest(Strict):
    decision: Decision
    notes: str | None = Field(default=None, max_length=MAX_NOTES_LENGTH)

    @model_validator(mode="after")
    def _revise_needs_notes(self) -> "ResumeRequest":
        if self.decision == "revise" and not (self.notes and self.notes.strip()):
            raise ValueError("notes are required when the decision is revise")
        return self


class WorkflowView(BaseModel):
    thread_id: str
    status: WorkflowStatus
    revision: int
    interrupt: dict[str, Any] | None
    plan: dict[str, Any] | None
    proposal: dict[str, Any] | None
    officer_summary: str | None
    validation: list[dict[str, Any]]
    nodes: list[str]
    steps: list[dict[str, Any]]
    policy_snapshot: dict[str, Any] | None
    error: str | None
    model: str
    usage: dict[str, Any] | None = None  # LLM tokens for the whole run; None when no LLM was called
    started_at: str | None
    completed_at: str | None
    duration_ms: int | None
