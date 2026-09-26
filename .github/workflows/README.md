# CI workflows

GitHub Actions owned by Member 4 (D). `ci.yml` runs on pushes and PRs to `main`; its `backend` job restores, builds (Release, warnings as errors) and tests the .NET solution. The web, mobile and agent jobs come later (plan §17.2).
