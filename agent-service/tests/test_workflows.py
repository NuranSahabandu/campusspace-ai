from fastapi.testclient import TestClient

from tests.conftest import TEST_SERVICE_KEY


def test_post_workflows_without_key_returns_401(client: TestClient) -> None:
    response = client.post("/workflows")

    assert response.status_code == 401
    assert response.json() == {"detail": "Invalid service key"}


def test_post_workflows_with_wrong_key_returns_401(client: TestClient) -> None:
    response = client.post("/workflows", headers={"X-Service-Key": "wrong"})

    assert response.status_code == 401
    assert response.json() == {"detail": "Invalid service key"}


def test_post_workflows_with_correct_key_returns_501(client: TestClient) -> None:
    response = client.post("/workflows", headers={"X-Service-Key": TEST_SERVICE_KEY})

    assert response.status_code == 501
    assert response.json() == {"detail": "Workflow engine arrives in Phase 3"}
