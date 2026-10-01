"""Fake models, never a real call and never a key: a planner model with the same
with_structured_output(Plan, include_raw=True) surface as ChatGoogleGenerativeAI, and a tool-calling
chat model that runs through the real create_agent (LLM workers)."""

import threading
from collections.abc import Callable
from typing import Any
from uuid import uuid4

from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.messages import AIMessage, BaseMessage
from langchain_core.outputs import ChatGeneration, ChatResult
from langchain_core.utils.function_calling import convert_to_openai_tool
from pydantic import ConfigDict, Field, ValidationError

from app.schemas import Plan, PlanStep
from app.workers.common import parse_brief
from app.workers.supervisor import INSTRUCTIONS
from tests.fake_api import BUILDINGS, ROOMS

USAGE = {"input_tokens": 900, "output_tokens": 120, "total_tokens": 1020}
REQUEST_42_EQUIPMENT = [
    {"type_code": "MIC-WIRELESS", "quantity": 2},
    {"type_code": "PROJ-PORTABLE", "quantity": 1},
]


class Invalid:
    """A response whose structured output did not parse (parsed None + parsing_error)."""


INVALID = Invalid()


class Sleep:
    """A response that blocks until released (or the test ends): a hung Gemini call."""

    def __init__(self, seconds: float = 5.0) -> None:
        self.seconds = seconds
        self.release = threading.Event()


Response = Plan | Invalid | Sleep | BaseException | Callable[[list], Plan]


def plan(
    soft_preferences: list[str] | None = None,
    *,
    features: list[str] | None = None,
    equipment: list[dict[str, Any]] | None = None,
    steps: list[tuple[str, str]] | None = None,
) -> Plan:
    """A plan that matches tests.fake_api's request 42 unless told otherwise."""
    return Plan.model_validate(
        {
            "required_features": ["computers", "projector"] if features is None else features,
            "equipment": REQUEST_42_EQUIPMENT if equipment is None else equipment,
            "soft_preferences": soft_preferences or [],
            "steps": [
                PlanStep(agent=a, task=t).model_dump()
                for a, t in (steps or [(a, INSTRUCTIONS[a]) for a in INSTRUCTIONS])
            ],
        }
    )


class FakePlannerModel:
    def __init__(self, *responses: Response, usage: dict[str, int] | None = USAGE) -> None:
        self.responses = list(responses)
        self.usage = usage
        self.calls: list[list[tuple[str, str]]] = []
        self.schema: Any = None

    def with_structured_output(self, schema: Any, include_raw: bool = False) -> "FakePlannerModel":
        assert include_raw, "the planner needs the raw message for usage_metadata"
        self.schema = schema
        return self

    def human_messages(self, call: int = -1) -> str:
        return "\n".join(text for role, text in self.calls[call] if role == "human")

    def invoke(self, messages: list[tuple[str, str]]) -> dict[str, Any]:
        self.calls.append(list(messages))
        response = self.responses.pop(0) if len(self.responses) > 1 else self.responses[0]
        if callable(response) and not isinstance(response, (Plan, BaseException)):
            response = response(messages)
        if isinstance(response, Sleep):
            response.release.wait(response.seconds)
            response = plan()
        if isinstance(response, BaseException):
            raise response
        raw = (
            AIMessage(content="{}", usage_metadata=self.usage)
            if self.usage
            else AIMessage(content='{"steps": []}')
        )
        if isinstance(response, Invalid):
            try:
                Plan.model_validate({})
            except ValidationError as exc:
                return {"raw": raw, "parsed": None, "parsing_error": exc}
        return {"raw": raw, "parsed": response, "parsing_error": None}


# ---------- a fake tool-calling chat model for create_agent workers ----------

TURN_USAGE = {"input_tokens": 400, "output_tokens": 40, "total_tokens": 440}
Turn = AIMessage | Sleep | BaseException | Callable[[list[BaseMessage]], Any]


def _tool_name(tool: Any) -> str:
    return convert_to_openai_tool(tool)["function"]["name"]


class FakeToolModel(BaseChatModel):
    """Scripted turns through the REAL create_agent: an AIMessage (tool calls or the VenueResult
    call), a callable(messages) -> AIMessage, Sleep (a hung call) or an exception. Records every
    message list it received and the tools bound to it. Never a network call, never a key."""

    model_config = ConfigDict(arbitrary_types_allowed=True)

    turns: list[Any] = Field(default_factory=list)
    usage: dict[str, int] | None = TURN_USAGE
    calls: list[list[BaseMessage]] = Field(default_factory=list)
    bound: list[list[str]] = Field(default_factory=list)
    tool_choice: list[Any] = Field(default_factory=list)

    @property
    def _llm_type(self) -> str:
        return "fake-tool-model"

    def bind_tools(self, tools: Any, *, tool_choice: Any = None, **_: Any) -> "FakeToolModel":
        self.bound.append([_tool_name(t) for t in tools])
        self.tool_choice.append(tool_choice)
        return self

    def _generate(
        self,
        messages: list[BaseMessage],
        stop: list[str] | None = None,
        run_manager: Any = None,
        **kwargs: Any,
    ) -> ChatResult:
        self.calls.append(list(messages))
        if not self.turns:
            raise AssertionError("FakeToolModel ran out of scripted turns")
        turn = self.turns.pop(0)
        if isinstance(turn, Sleep):
            turn.release.wait(turn.seconds)
            raise TimeoutError("released after the test")
        if isinstance(turn, BaseException):
            raise turn
        message = turn(messages) if callable(turn) else turn
        message = message.model_copy(
            update={
                "tool_calls": [
                    c | {"id": c.get("id") or f"call_{uuid4().hex[:8]}"} for c in message.tool_calls
                ],  # fmt: skip
                "usage_metadata": self.usage,
            }
        )
        return ChatResult(generations=[ChatGeneration(message=message)])

    def human_texts(self) -> list[str]:
        return [str(m.content) for call in self.calls for m in call if m.type == "human"]

    def all_texts(self) -> list[str]:
        return [str(m.content) for call in self.calls for m in call]


def brief_of(messages: list[BaseMessage]) -> dict[str, Any]:
    task = next(str(m.content) for m in messages if m.type == "human")
    return parse_brief(task.split("\n\nYour previous answer was rejected")[0])


def call(name: str, **args: Any) -> AIMessage:
    return AIMessage(content="", tool_calls=[{"name": name, "args": args, "id": None}])


def search(**override: Any) -> Callable[[list[BaseMessage]], AIMessage]:
    """search_available_rooms with the brief's own values, unless overridden."""

    def turn(messages: list[BaseMessage]) -> AIMessage:
        brief = brief_of(messages)
        args = {
            "min_capacity": brief["attendees"],
            "features": brief["required_features"],
            "start_iso": brief["start"],
            "end_iso": brief["end"],
        }
        return call("search_available_rooms", **(args | override))

    return turn


def details(room_id: int) -> AIMessage:
    return call("get_room_details", room_id=room_id)


def room_option(code: str, reason: str | None = None) -> dict[str, Any]:
    rid, code, name, _, capacity, building, features = next(r for r in ROOMS if r[1] == code)
    return {
        "room_id": rid,
        "code": code,
        "name": name,
        "capacity": capacity,
        "building": BUILDINGS[building]["name"],
        "features": features,
        "reason": reason or f"{capacity} seats for the attendees; {BUILDINGS[building]['name']}",
    }


def answer(*options: str | dict[str, Any], unmet: str | None = None) -> AIMessage:
    """The VenueResult structured-output call; room codes become options from the seed data."""
    opts = [room_option(o) if isinstance(o, str) else o for o in options]
    return call("VenueResult", options=opts, unmet=unmet)


def prefer_new_building(messages: list[BaseMessage]) -> AIMessage:
    """Picks N201 when the brief's soft_preferences mention the New Building, else A301."""
    prefs = " ".join(brief_of(messages)["soft_preferences"]).lower()
    if "new building" in prefs:
        return answer(room_option("N201", "60 seats, computers and projector; New Building as "
                                          "preferred"), "A301")  # fmt: skip
    return answer("A301", "N201")


# ---------- equipment worker turns ----------


def check_stock(*codes: str, **override: Any) -> Callable[[list[BaseMessage]], AIMessage]:
    """check_equipment_availability for these codes (default: every requested code) and the
    brief's window, unless overridden."""

    def turn(messages: list[BaseMessage]) -> AIMessage:
        brief = brief_of(messages)
        args = {
            "codes": list(codes) or [line["code"] for line in brief["lines"]],
            "start_iso": brief["start"],
            "end_iso": brief["end"],
        }
        return call("check_equipment_availability", **(args | override))

    return turn


def substitutes(code: str) -> AIMessage:
    return call("get_substitutes", code=code)


def allocation(
    *lines: tuple[str, int, str],
    substitutions: list[dict[str, Any]] | None = None,
    unmet: list[str] | None = None,
) -> AIMessage:
    """The EquipmentResult structured-output call; lines are (type_code, qty, source)."""
    return call(
        "EquipmentResult",
        lines=[{"type_code": c, "qty": q, "source": s} for c, q, s in lines],
        substitutions=substitutions or [],
        unmet=unmet or [],
    )


DEMO_ALLOCATION = (("MIC-WIRELESS", 2, "portable"), ("PROJ-PORTABLE", 0, "room_builtin"))


def mic_substitute(reason: str = "only 1 MIC-WIRELESS available; 8 MIC-WIRED available") -> dict:
    return {"requested_code": "MIC-WIRELESS", "substitute_code": "MIC-WIRED", "qty": 2,
            "reason": reason}  # fmt: skip


# ---------- policy worker turns ----------


def quote(**override: Any) -> Callable[[list[BaseMessage]], AIMessage]:
    """calculate_quote with exactly the brief's arguments, unless overridden."""

    def turn(messages: list[BaseMessage]) -> AIMessage:
        brief = brief_of(messages)
        args = {
            "room_id": brief["room_id"],
            "start_iso": brief["start"],
            "end_iso": brief["end"],
            "requester_role": brief["requester_role"],
            "equipment": brief["priced_lines"],
        }
        return call("calculate_quote", **(args | override))

    return turn


def check_policy() -> AIMessage:
    return call("check_policy")


DEMO_SUMMARY = (
    "A301 fits the 45 attendees with computers and a projector. Two wireless mics are portable; "
    "the room's projector covers the portable one. Total LKR 5,500.00 against a budget of "
    "LKR 8,000.00: within budget. Free cancellation until 24 h before the start."
)
DEMO_FLAGS = ["Within budget: LKR 5,500.00 vs LKR 8,000.00", "PROJ-PORTABLE built into A301"]


def policy_answer(summary: str = DEMO_SUMMARY, flags: list[str] | None = None) -> AIMessage:
    """The PolicyAnswer structured-output call."""
    return call(
        "PolicyAnswer",
        policy_flags=DEMO_FLAGS if flags is None else flags,
        officer_summary=summary,
    )
