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

Regenerate them after a contract change in `agent-service/app/schemas.py`. From `agent-service/`, run
`PYTHONPATH=. uv run python gen.py`, where `gen.py` does this for each row of the table:

```python
from tests.harness import Harness
from tests.fake_api import FakeCampusApi

h = Harness(tmp_dir / "cp.sqlite", api=FakeCampusApi())  # FakeCampusApi(down={"policy"}) for agent-failed
thread = h.start(42)                                     # 43 for the lecturer
Path("agent-awaiting-approval-student.json").write_text(json.dumps(h.view(thread).model_dump(mode="json"), indent=2) + "\n")
h.close()
```
