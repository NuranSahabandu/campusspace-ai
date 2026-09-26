# CI workflows

GitHub Actions owned by Member 4 (D). `ci.yml` runs on pushes and PRs to `main`; its `backend` job restores, builds (Release, warnings as errors) and tests the .NET solution. Its `agent` job syncs `agent-service/` with uv (`--locked`), then runs `ruff check` and `pytest` without secrets. The web and mobile jobs come later (plan §17.2).
