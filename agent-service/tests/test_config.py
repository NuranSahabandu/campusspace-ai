from pathlib import Path

import pytest
from pydantic import ValidationError

import app.config
from app.config import DEFAULT_CHECKPOINT_PATH, ROOT_ENV, Settings
from tests.conftest import TEST_SERVICE_KEY, TEST_TOOLS_KEY, make_settings


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
    (tmp_path / ".env").write_text(
        f"AgentService__ServiceKey={'d' * 64}\nAgentTools__Key={'e' * 64}\n"
    )
    repo_env = repo / ".env"
    repo_env.write_text(f"AgentService__ServiceKey={'r' * 64}\nAgentTools__Key={'s' * 64}\n")
    monkeypatch.chdir(repo)
    # Stand-in for ROOT_ENV so the real .env is never read.
    monkeypatch.setitem(Settings.model_config, "env_file", repo_env)

    settings = Settings()

    assert settings.service_key.get_secret_value() == "r" * 64


def test_missing_tools_key_raises(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("AgentService__ServiceKey", TEST_SERVICE_KEY)

    with pytest.raises(ValidationError, match="AgentTools__Key"):
        Settings(_env_file=None)


def test_short_tools_key_raises_without_echoing_it(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("AgentService__ServiceKey", TEST_SERVICE_KEY)
    monkeypatch.setenv("AgentTools__Key", "y" * 31)

    with pytest.raises(ValidationError, match="AgentTools__Key must be at least 32") as exc:
        Settings(_env_file=None)
    assert "y" * 31 not in str(exc.value)


def test_equal_service_and_tools_keys_raise(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError, match="must be different") as exc:
        make_settings(monkeypatch, AgentTools__Key=TEST_SERVICE_KEY)
    assert TEST_SERVICE_KEY not in str(exc.value)


def test_checkpoint_path_is_anchored_and_overridable(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    agent_service = Path(app.config.__file__).resolve().parents[1]
    assert DEFAULT_CHECKPOINT_PATH == agent_service / "data" / "checkpoints.sqlite"
    monkeypatch.delenv("AGENT_CHECKPOINT_PATH")
    assert make_settings(monkeypatch).checkpoint_path == DEFAULT_CHECKPOINT_PATH

    custom = tmp_path / "cp.sqlite"
    settings = make_settings(monkeypatch, AGENT_CHECKPOINT_PATH=str(custom))

    assert settings.checkpoint_path == custom
    assert settings.agent_tools_key.get_secret_value() == TEST_TOOLS_KEY


def test_empty_checkpoint_path_means_the_default(monkeypatch: pytest.MonkeyPatch) -> None:
    assert make_settings(monkeypatch, AGENT_CHECKPOINT_PATH="").checkpoint_path == (
        DEFAULT_CHECKPOINT_PATH
    )


# ---------- AGENT_LLM_AGENTS and the Gemini key ----------

FAKE_GOOGLE_KEY = "fake-google-key-" + "g" * 24


def test_no_llm_agents_needs_no_google_key(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = make_settings(monkeypatch, AGENT_LLM_AGENTS="")

    assert settings.llm_agents == frozenset()
    assert settings.google_api_key is None
    assert settings.agent_mode("supervisor") == "stub"
    assert settings.model_label() == "planner=stub; workers=stub"


def test_empty_google_key_counts_as_missing(monkeypatch: pytest.MonkeyPatch) -> None:
    assert make_settings(monkeypatch, GOOGLE_API_KEY="  ").google_api_key is None


def test_llm_supervisor_without_a_key_fails_startup(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError, match="GOOGLE_API_KEY is required") as exc:
        make_settings(monkeypatch, AGENT_LLM_AGENTS="supervisor")
    assert "supervisor" in str(exc.value)


def test_llm_errors_never_echo_the_google_key(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError) as exc:
        make_settings(
            monkeypatch, AGENT_LLM_AGENTS="supervisor,nope", GOOGLE_API_KEY=FAKE_GOOGLE_KEY
        )
    assert FAKE_GOOGLE_KEY not in str(exc.value)


def test_llm_supervisor_with_a_key(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = make_settings(
        monkeypatch, AGENT_LLM_AGENTS=" Supervisor , ", GOOGLE_API_KEY=FAKE_GOOGLE_KEY
    )

    assert settings.llm_agents == frozenset({"supervisor"})
    assert settings.agent_mode("supervisor") == "llm"
    assert settings.agent_mode("venue_matching") == "stub"
    assert settings.model_label() == "planner=gemini-3.5-flash; workers=stub"
    assert FAKE_GOOGLE_KEY not in repr(settings)


def test_unknown_llm_agent_is_rejected(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError, match="unknown agent\\(s\\) planner; allowed: supervisor"):
        make_settings(monkeypatch, AGENT_LLM_AGENTS="planner", GOOGLE_API_KEY=FAKE_GOOGLE_KEY)


def test_worker_without_an_llm_implementation_is_rejected(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    with pytest.raises(ValidationError, match="venue_matching has no LLM implementation yet"):
        make_settings(
            monkeypatch, AGENT_LLM_AGENTS="venue_matching", GOOGLE_API_KEY=FAKE_GOOGLE_KEY
        )


def test_thinking_levels_default_low_and_can_be_overridden(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    defaults = make_settings(monkeypatch, PLANNER_THINKING="", WORKER_THINKING="")
    assert (defaults.planner_thinking, defaults.worker_thinking) == ("low", "minimal")

    custom = make_settings(monkeypatch, PLANNER_THINKING=" Medium ", WORKER_THINKING="low")
    assert (custom.planner_thinking, custom.worker_thinking) == ("medium", "low")


def test_an_unknown_thinking_level_is_rejected(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError, match="minimal', 'low', 'medium' or 'high'"):
        make_settings(monkeypatch, WORKER_THINKING="none")


def test_model_ids_default_to_the_labs_and_can_be_overridden(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    defaults = make_settings(monkeypatch, PLANNER_MODEL="", WORKER_MODEL="")
    assert (defaults.planner_model, defaults.worker_model) == (
        "gemini-3.5-flash",
        "gemini-3.5-flash-lite",
    )

    custom = make_settings(
        monkeypatch,
        PLANNER_MODEL="gemini-x",
        WORKER_MODEL="gemini-y",
        AGENT_LLM_AGENTS="supervisor",
        GOOGLE_API_KEY=FAKE_GOOGLE_KEY,
    )
    assert (custom.planner_model, custom.worker_model) == ("gemini-x", "gemini-y")
    assert custom.model_label() == "planner=gemini-x; workers=stub"


def test_model_label_fits_the_dotnet_column(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = make_settings(
        monkeypatch,
        PLANNER_MODEL="p" * 120,
        AGENT_LLM_AGENTS="supervisor",
        GOOGLE_API_KEY=FAKE_GOOGLE_KEY,
    )

    assert len(settings.model_label()) == 100
