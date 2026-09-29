"""A fake planner model: the same with_structured_output(Plan, include_raw=True) surface as
ChatGoogleGenerativeAI, with canned responses. Never a real call, never a key."""

import threading
from collections.abc import Callable
from typing import Any

from langchain_core.messages import AIMessage
from pydantic import ValidationError

from app.schemas import Plan, PlanStep
from app.workers.supervisor import INSTRUCTIONS

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
