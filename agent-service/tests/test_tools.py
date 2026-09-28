from decimal import Decimal

import httpx
import pytest
from pydantic import SecretStr

from app.tools import (
    ToolClient,
    build_tools,
    is_error,
    is_unavailable,
    parse_json,
    recording,
)
from tests.fake_api import END, START, FakeCampusApi
from tests.keys import TEST_TOOLS_KEY


@pytest.fixture
def api() -> FakeCampusApi:
    return FakeCampusApi()


@pytest.fixture
def tools(api: FakeCampusApi):
    client = ToolClient("http://api.test", SecretStr(TEST_TOOLS_KEY), transport=api.transport())
    yield build_tools(client)
    client.close()


def test_calls_send_the_agent_key_and_offset_times(api: FakeCampusApi, tools) -> None:
    out = tools["search_available_rooms"].invoke(
        {
            "min_capacity": 45,
            "features": ["computers", "projector"],
            "start_iso": "2026-10-16T14:00:00+05:30",
            "end_iso": "2026-10-16T17:00:00+05:30",
        }
    )

    rooms = parse_json(out)
    assert [r["code"] for r in rooms["items"]] == ["A301", "N201"]
    sent = api.calls[-1]
    assert sent.headers["X-Agent-Key"] == TEST_TOOLS_KEY
    assert "%2B05%3A30" in str(sent.url)  # "+" is encoded, so .NET sees the offset


def test_money_is_parsed_as_decimal(tools) -> None:
    out = tools["calculate_quote"].invoke(
        {
            "room_id": 1,
            "start_iso": START,
            "end_iso": END,
            "requester_role": "Student",
            "equipment": [{"code": "MIC-WIRELESS", "quantity": 2}],
        }
    )

    quote = parse_json(out)
    assert quote["total"] == Decimal("5500.00")
    assert isinstance(quote["total"], Decimal)
    assert [line["description"] for line in quote["lines"]] == [
        "Computer lab A301, 3 h @ LKR 1,500",
        "Wireless microphone x2 @ LKR 500",
    ]


def test_problem_details_become_a_domain_tool_error(tools) -> None:
    out = tools["search_available_rooms"].invoke(
        {
            "min_capacity": 10,
            "features": [],
            "start_iso": "2026-10-18T10:00:00+05:30",  # Sunday
            "end_iso": "2026-10-18T12:00:00+05:30",
        }
    )

    assert is_error(out) and not is_unavailable(out)
    assert out == (
        "TOOL_ERROR: HTTP 400: One or more validation errors occurred.; "
        "Start: The campus is closed on Sundays"
    )


def test_not_found_is_a_domain_tool_error(tools) -> None:
    out = tools["get_request_context"].invoke({"request_id": 999})

    assert out == "TOOL_ERROR: HTTP 404: Not Found"


@pytest.mark.parametrize(
    ("setup", "expected"),
    [
        (
            lambda api: api.down.add("*"),
            "TOOL_ERROR: unavailable: ConnectError: Connection refused",
        ),
        (
            lambda api: api.status_override.update({"policy": 503}),
            "TOOL_ERROR: unavailable: HTTP 503",
        ),
    ],
)
def test_network_and_server_errors_are_unavailable(
    api: FakeCampusApi, tools, setup, expected
) -> None:
    setup(api)

    out = tools["get_policy"].invoke({})

    assert out == expected
    assert is_unavailable(out)


def test_timeout_is_unavailable() -> None:
    def slow(request: httpx.Request) -> httpx.Response:
        raise httpx.ReadTimeout("slow", request=request)

    client = ToolClient(
        "http://api.test", SecretStr(TEST_TOOLS_KEY), transport=httpx.MockTransport(slow)
    )

    out = build_tools(client)["get_policy"].invoke({})

    assert out == "TOOL_ERROR: unavailable: timed out after 10 s"


def test_wrong_key_is_unavailable_and_never_echoed() -> None:
    api = FakeCampusApi()
    client = ToolClient("http://api.test", SecretStr("w" * 64), transport=api.transport())

    out = build_tools(client)["get_policy"].invoke({})

    assert out == "TOOL_ERROR: unavailable: HTTP 401"
    assert "w" * 64 not in out


def test_bad_arguments_never_reach_dotnet(api: FakeCampusApi, tools) -> None:
    with recording() as rec:
        out = tools["search_available_rooms"].invoke(
            {"min_capacity": 0, "features": [], "start_iso": "2026-10-16T14:00", "end_iso": END}
        )

    assert out.startswith("TOOL_ERROR: invalid arguments:")
    assert "min_capacity" in out and "start" in out
    assert api.calls == []
    assert rec.calls[0].succeeded is False and rec.calls[0].error


def test_recorder_keeps_summaries_and_seen_ids_but_no_notes(tools) -> None:
    with recording() as rec:
        tools["get_request_context"].invoke({"request_id": 42})
        tools["search_available_rooms"].invoke(
            {"min_capacity": 45, "features": ["projector"], "start_iso": START, "end_iso": END}
        )
        tools["check_equipment_availability"].invoke(
            {"codes": ["MIC-WIRELESS"], "start_iso": START, "end_iso": END}
        )
        tools["get_substitutes"].invoke({"code": "MIC-WIRELESS"})

    context_call = rec.calls[0]
    assert context_call.tool_name == "get_request_context"
    assert context_call.result_summary is not None
    assert context_call.result_summary["notes"] == "<omitted>"
    assert "15 minutes" not in repr(rec.calls)  # raw notes never reach the trace
    assert rec.seen_room_ids == [1, 6, 4, 9, 3, 7, 5]
    assert rec.seen_equipment_codes == ["MIC-WIRELESS", "MIC-WIRED"]
    assert all(c.succeeded and c.duration_ms >= 0 for c in rec.calls)


def test_tools_outside_a_recorder_are_not_recorded(tools) -> None:
    out = tools["list_feature_catalog"].invoke({})

    assert "projector" in out
