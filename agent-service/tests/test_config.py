from pathlib import Path

import pytest
from pydantic import ValidationError

import app.config
from app.config import ROOT_ENV, Settings
from tests.conftest import TEST_SERVICE_KEY, make_settings


def test_short_service_key_raises(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("AgentService__ServiceKey", "x" * 31)

    with pytest.raises(ValidationError, match="at least 32 characters") as exc:
        Settings(_env_file=None)
    assert "x" * 31 not in str(exc.value)


def test_missing_service_key_raises() -> None:
    with pytest.raises(ValidationError, match="AgentService__ServiceKey"):
        Settings(_env_file=None)


def test_env_names_match_dotnet(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = make_settings(
        monkeypatch,
        AgentTools__Key="k" * 32,
        GOOGLE_API_KEY="g",
        API_BASE_URL="http://api.test:5080",
    )

    assert settings.service_key.get_secret_value() == TEST_SERVICE_KEY
    assert settings.agent_tools_key is not None
    assert settings.agent_tools_key.get_secret_value() == "k" * 32
    assert settings.google_api_key is not None
    assert settings.api_base_url == "http://api.test:5080"


def test_root_env_is_anchored_to_repo_root() -> None:
    repo_root = Path(app.config.__file__).resolve().parents[2]

    assert ROOT_ENV.is_absolute()
    assert ROOT_ENV == repo_root / ".env"
    assert Settings.model_config["env_file"] == ROOT_ENV


def test_loads_env_file_when_cwd_is_repo_root(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    # tmp_path/.env is a decoy where a cwd-relative "../.env" would resolve from repo/.
    repo = tmp_path / "repo"
    repo.mkdir()
    (tmp_path / ".env").write_text(f"AgentService__ServiceKey={'d' * 64}\n")
    repo_env = repo / ".env"
    repo_env.write_text(f"AgentService__ServiceKey={'r' * 64}\n")
    monkeypatch.chdir(repo)
    # Stand-in for ROOT_ENV so the real .env is never read.
    monkeypatch.setitem(Settings.model_config, "env_file", repo_env)

    settings = Settings()

    assert settings.service_key.get_secret_value() == "r" * 64
