"""The /workflows HTTP contract that 3.3's AgentClient codes against."""

from uuid import uuid4

import pytest
from fastapi.testclient import TestClient

from tests.fake_api import FakeCampusApi
from tests.harness import HAPPY_NODES
from tests.keys import TEST_SERVICE_KEY, TEST_TOOLS_KEY

AUTH = {"X-Service-Key": TEST_SERVICE_KEY}
SOME_ID = "7f3c9e2a-1b2c-4d5e-8f90-0123456789ab"


def start(client: TestClient, request_id: int = 42) -> str:
    thread_id = str(uuid4())
    response = client.post(
        "/workflows", json={"thread_id": thread_id, "request_id": request_id}, headers=AUTH
    )
    assert response.status_code == 202, response.text
    return thread_id


# ---------- X-Service-Key ----------

ROUTES = [
    ("POST", "/workflows", {"thread_id": SOME_ID, "request_id": 42}),
    ("GET", f"/workflows/{SOME_ID}", None),
    ("POST", f"/workflows/{SOME_ID}/resume", {"decision": "approve"}),
]


@pytest.mark.parametrize(("method", "path", "body"), ROUTES)
@pytest.mark.parametrize(
    "headers", [{}, {"X-Service-Key": "wrong"}, {"X-Service-Key": TEST_TOOLS_KEY}]
)
def test_every_workflow_route_needs_the_service_key(
    client: TestClient, fake_api: FakeCampusApi, method, path, body, headers
) -> None:
    response = client.request(method, path, json=body, headers=headers)

    assert response.status_code == 401
    assert response.json() == {"detail": "Invalid service key"}
    assert fake_api.calls == []  # nothing ran


def test_missing_key_wins_over_a_bad_body(client: TestClient) -> None:
    assert client.post("/workflows", json={"thread_id": "nope"}).status_code == 401


def test_health_needs_no_key(client: TestClient) -> None:
    assert client.get("/health").status_code == 200


# ---------- start ----------


def test_start_returns_202_and_the_run_pauses(client: TestClient) -> None:
    thread_id = str(uuid4())

    response = client.post(
        "/workflows", json={"thread_id": thread_id, "request_id": 42}, headers=AUTH
    )

    assert response.status_code == 202
    assert response.json() == {"thread_id": thread_id, "status": "running"}
    view = client.get(f"/workflows/{thread_id}", headers=AUTH).json()
    assert view["status"] == "awaiting_approval"
    assert view["nodes"] == HAPPY_NODES
    assert view["proposal"]["quote"]["total"] == "5500.00"
    assert view["model"] == "planner=stub; workers=stub"
    assert view["usage"] is None  # stub agents make no LLM calls
    assert len(view["validation"]) == 12 and all(v["passed"] for v in view["validation"])
    assert set(view) == {
        "thread_id",
        "status",
        "revision",
        "interrupt",
        "plan",
        "proposal",
        "officer_summary",
        "validation",
        "nodes",
        "steps",
        "policy_snapshot",
        "error",
        "model",
        "usage",
        "started_at",
        "completed_at",
        "duration_ms",
    }
    assert view["started_at"] == "2026-10-01T03:30:00.000Z"


@pytest.mark.parametrize(
    "body",
    [
        {"thread_id": "not-a-uuid", "request_id": 42},
        {"thread_id": SOME_ID, "request_id": 0},
        {"thread_id": SOME_ID},
        {"thread_id": SOME_ID, "request_id": 42, "request": {"notes": "x"}},
    ],
)
def test_bad_start_body_is_400(client: TestClient, body) -> None:
    response = client.post("/workflows", json=body, headers=AUTH)

    assert response.status_code == 400
    assert response.json()["detail"]


def test_duplicate_thread_is_409(client: TestClient) -> None:
    thread_id = start(client)

    response = client.post(
        "/workflows", json={"thread_id": thread_id, "request_id": 42}, headers=AUTH
    )

    assert response.status_code == 409
    assert response.json() == {"detail": "A workflow with this thread_id already exists"}


# ---------- status ----------


def test_unknown_thread_is_404(client: TestClient) -> None:
    assert client.get(f"/workflows/{SOME_ID}", headers=AUTH).status_code == 404


def test_bad_uuid_in_the_path_is_400(client: TestClient) -> None:
    assert client.get("/workflows/abc", headers=AUTH).status_code == 400


def test_failed_run_reports_the_reason(client: TestClient, fake_api: FakeCampusApi) -> None:
    fake_api.requests.clear()

    view = client.get(f"/workflows/{start(client)}", headers=AUTH).json()

    assert view["status"] == "failed"
    assert view["error"] == "Booking request not found"
    assert view["completed_at"] and view["duration_ms"] is not None


# ---------- resume ----------


def test_revise_then_approve_over_http(client: TestClient) -> None:
    thread_id = start(client)

    revised = client.post(
        f"/workflows/{thread_id}/resume",
        json={"decision": "revise", "notes": "Use the New Building."},
        headers=AUTH,
    )
    assert revised.status_code == 202
    assert revised.json() == {"thread_id": thread_id, "status": "running"}
    view = client.get(f"/workflows/{thread_id}", headers=AUTH).json()
    assert view["status"] == "awaiting_approval"
    assert view["proposal"]["room_code"] == "N201"
    assert {v["attempt"] for v in view["validation"]} == {1, 2}

    approved = client.post(
        f"/workflows/{thread_id}/resume", json={"decision": "approve"}, headers=AUTH
    )
    assert approved.status_code == 202
    done = client.get(f"/workflows/{thread_id}", headers=AUTH).json()
    assert done["status"] == "completed"
    assert done["nodes"][-1] == "finalize"


def test_resume_when_not_paused_is_409(client: TestClient) -> None:
    thread_id = start(client)
    client.post(f"/workflows/{thread_id}/resume", json={"decision": "reject"}, headers=AUTH)

    response = client.post(
        f"/workflows/{thread_id}/resume", json={"decision": "approve"}, headers=AUTH
    )

    assert response.status_code == 409
    assert response.json() == {"detail": "The workflow is rejected, not awaiting_approval"}


def test_resume_unknown_thread_is_404(client: TestClient) -> None:
    response = client.post(
        f"/workflows/{SOME_ID}/resume", json={"decision": "approve"}, headers=AUTH
    )

    assert response.status_code == 404


@pytest.mark.parametrize(
    "body",
    [
        {"decision": "revise"},
        {"decision": "revise", "notes": "   "},
        {"decision": "book-it-now"},
        {"decision": "approve", "notes": "x" * 1001},
    ],
)
def test_bad_resume_body_is_400(client: TestClient, body) -> None:
    thread_id = start(client)

    response = client.post(f"/workflows/{thread_id}/resume", json=body, headers=AUTH)

    assert response.status_code == 400
    view = client.get(f"/workflows/{thread_id}", headers=AUTH).json()
    assert view["status"] == "awaiting_approval"  # nothing changed


def test_cancel_over_http(client: TestClient) -> None:
    thread_id = start(client)

    client.post(f"/workflows/{thread_id}/resume", json={"decision": "cancel"}, headers=AUTH)

    assert client.get(f"/workflows/{thread_id}", headers=AUTH).json()["status"] == "cancelled"
