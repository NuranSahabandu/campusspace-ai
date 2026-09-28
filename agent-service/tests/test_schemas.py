from decimal import Decimal

import pytest
from pydantic import ValidationError

from app.schemas import (
    EquipmentLine,
    PlanStep,
    Quote,
    ResumeRequest,
    RoomSearchArgs,
    StartWorkflowRequest,
    ToolCallTrace,
    VenueResult,
)


def test_agent_outputs_forbid_extra_fields() -> None:
    with pytest.raises(ValidationError, match="Extra inputs are not permitted"):
        VenueResult.model_validate({"options": [], "unmet": None, "status": "approved"})


def test_plan_step_cannot_invent_a_worker() -> None:
    with pytest.raises(ValidationError):
        PlanStep(agent="booking_executor", task="book it")  # type: ignore[arg-type]


@pytest.mark.parametrize(
    ("source", "qty", "ok"),
    [
        ("room_builtin", 0, True),
        ("room_builtin", 1, False),
        ("portable", 0, False),
        ("portable", 2, True),
        ("substitute", 2, True),
        ("borrowed", 2, False),
    ],
)
def test_room_builtin_only_with_qty_zero(source: str, qty: int, ok: bool) -> None:
    data = {"type_code": "PROJ-PORTABLE", "qty": qty, "source": source}
    if ok:
        assert EquipmentLine.model_validate(data).qty == qty
    else:
        with pytest.raises(ValidationError):
            EquipmentLine.model_validate(data)


def test_money_serializes_as_exact_two_dp_string() -> None:
    quote = Quote(
        lines=[],
        subtotal=Decimal("5500"),
        discount=Decimal("0"),
        exempt=False,
        total=Decimal("5500.0"),
    )

    dumped = quote.model_dump(mode="json")

    assert dumped["total"] == "5500.00"
    assert dumped["discount"] == "0.00"
    assert Quote.model_validate(dumped).total == Decimal("5500.00")


@pytest.mark.parametrize(
    "instant", ["2026-10-16T14:00:00", "2026-10-16 14:00+05:30", "16/10/2026", "2026-13-01T00:00Z"]
)
def test_tool_times_need_an_offset(instant: str) -> None:
    with pytest.raises(ValidationError):
        RoomSearchArgs(min_capacity=1, start=instant, end="2026-10-16T17:00:00+05:30")


def test_room_search_query_uses_dotnet_names() -> None:
    args = RoomSearchArgs(
        min_capacity=45,
        features=["computers", "projector"],
        start="2026-10-16T14:00:00+05:30",
        end="2026-10-16T17:00:00+05:30",
    )

    assert args.as_query() == {
        "start": "2026-10-16T14:00:00+05:30",
        "end": "2026-10-16T17:00:00+05:30",
        "minCapacity": 45,
        "page": 1,
        "pageSize": 100,
        "features": "computers,projector",
    }


def test_failed_tool_call_needs_an_error() -> None:
    with pytest.raises(ValidationError, match="needs an error"):
        ToolCallTrace(tool_name="x", args={}, result_summary=None, succeeded=False, duration_ms=1)


def test_resume_revise_needs_notes() -> None:
    with pytest.raises(ValidationError, match="notes are required"):
        ResumeRequest(decision="revise", notes="  ")
    assert ResumeRequest(decision="approve").notes is None


def test_start_request_needs_a_uuid() -> None:
    with pytest.raises(ValidationError):
        StartWorkflowRequest.model_validate({"thread_id": "not-a-uuid", "request_id": 1})
