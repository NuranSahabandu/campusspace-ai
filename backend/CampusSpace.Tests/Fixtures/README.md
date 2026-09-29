# Agent service fixtures

Verbatim `GET /workflows/{thread_id}` bodies from the Phase 3.2 agent service, not hand-written. They were generated with
`agent-service/tests/harness.py`: the real LangGraph graph and the real `WorkflowRunner` over `tests/fake_api.py`, the
seed-shaped fake of the .NET tool routes (fixed clock 2026-10-01 09:00 campus time). Each file is
`runner.view(thread_id).model_dump(mode="json")`, which is exactly what the route serialises.

| File | Harness run |
|------|-------------|
| `agent-awaiting-approval-student.json` | request 42 (plan §11 walkthrough): A301, 2 × MIC-WIRELESS, projector covered by the room |
| `agent-awaiting-approval-lecturer.json` | request 43 (lecturer, exempt) |
| `agent-failed.json` | request 42 with the policy tool down: `failed`, "policy unavailable (…)" |
| `agent-completed-student.json` | request 42, then resume `approve`: `completed` (finalize re-checked A301 and the mics) |
| `agent-completed-lecturer.json` | request 43, then resume `approve`: `completed` |
| `agent-revised-student.json` | request 42, then resume `revise` ("Use a lab in the New Building; A301 has AC maintenance."): `awaiting_approval` again with N201, validation attempt 2 |
| `agent-finalize-failed.json` | request 42, A301 made busy in the fake (`api.busy`), then resume `approve`: `failed`, "Final re-check failed: V02: …" |

Regenerate them after a contract change in `agent-service/app/schemas.py`. From `agent-service/`, run
`PYTHONPATH=. uv run python gen.py`, where `gen.py` does this for each row of the table:

```python
from tests.harness import Harness
from tests.fake_api import FakeCampusApi

api = FakeCampusApi()                                    # FakeCampusApi(down={"policy"}) for agent-failed
h = Harness(tmp_dir / "cp.sqlite", api=api)
thread = h.start(42)                                     # 43 for the lecturer
view = h.view(thread)                                    # the 3.3 rows (paused, or failed)
# 3.4 rows: view = h.resume(thread, "approve") or h.resume(thread, "revise", notes);
# for agent-finalize-failed first set api.busy = {"A301": [(start, end)]} with request 42's times.
Path("agent-awaiting-approval-student.json").write_text(json.dumps(view.model_dump(mode="json"), indent=2) + "\n")
h.close()
```
