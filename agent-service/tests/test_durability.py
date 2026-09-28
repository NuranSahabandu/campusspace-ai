"""A paused run survives a restart: a NEW graph and runner on the same SQLite file resume it."""

from pathlib import Path

from tests.fake_api import FakeCampusApi
from tests.harness import HAPPY_NODES, Harness


def test_paused_run_resumes_after_a_restart(tmp_path: Path) -> None:
    db = tmp_path / "checkpoints.sqlite"
    api = FakeCampusApi()
    before = Harness(db, api)
    thread_id = before.start()
    assert before.view(thread_id).status == "awaiting_approval"
    before.close()  # the process "stops": connection, graph and in-process state are gone

    after = Harness(db, api)
    try:
        paused = after.view(thread_id)
        assert paused.status == "awaiting_approval"
        assert paused.nodes == HAPPY_NODES
        assert paused.interrupt["quote"]["total"] == "5500.00"
        policy_calls = len(api.calls_to("policy"))

        done = after.resume(thread_id, "approve")

        assert done.status == "completed"
        assert done.nodes == HAPPY_NODES + ["finalize"]
        assert len(api.calls_to("policy")) == policy_calls  # snapshot kept, never re-fetched
        assert done.policy_snapshot == paused.policy_snapshot
    finally:
        after.close()
