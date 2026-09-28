"""Settings from the repo-root .env and real environment variables (env vars win).

Variable names match the .NET API's (AgentService__ServiceKey etc.), so one .env serves both.
"""

from functools import lru_cache
from pathlib import Path

from pydantic import Field, SecretStr, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

# Anchored to this file (app/config.py -> agent-service/ -> repo root), not the working directory.
ROOT_ENV = Path(__file__).resolve().parents[2] / ".env"
# agent-service/data/ is git-ignored; anchored to this file like ROOT_ENV.
DEFAULT_CHECKPOINT_PATH = Path(__file__).resolve().parents[1] / "data" / "checkpoints.sqlite"

MIN_SERVICE_KEY_LENGTH = 32
MIN_TOOLS_KEY_LENGTH = 32


class Settings(BaseSettings):
    # hide_input_in_errors: a validation error must never echo a key value.
    model_config = SettingsConfigDict(env_file=ROOT_ENV, extra="ignore", hide_input_in_errors=True)

    service_key: SecretStr = Field(validation_alias="AgentService__ServiceKey")
    # Sent as X-Agent-Key to /internal/agent-tools. Every run needs it, so startup requires it.
    agent_tools_key: SecretStr = Field(validation_alias="AgentTools__Key")
    google_api_key: SecretStr | None = Field(default=None, validation_alias="GOOGLE_API_KEY")
    api_base_url: str = Field(default="http://localhost:5080", validation_alias="API_BASE_URL")
    # SqliteSaver file. Never InMemorySaver outside tests: a restart would lose paused approvals.
    checkpoint_path: Path = Field(
        default=DEFAULT_CHECKPOINT_PATH, validation_alias="AGENT_CHECKPOINT_PATH"
    )

    # Phase 3 agents are deterministic stubs; reported as AgentRuns.Model.
    agent_model_label: str = "stub"

    # Phase 4 (real Gemini agents) confirms both model ids against the Labs 05–07 client code.
    planner_model: str = "gemini-2.5-flash"
    worker_model: str = "gemini-2.5-flash-lite"

    @field_validator("service_key")
    @classmethod
    def _service_key_long_enough(cls, value: SecretStr) -> SecretStr:
        if len(value.get_secret_value()) < MIN_SERVICE_KEY_LENGTH:
            raise ValueError(
                f"AgentService__ServiceKey must be at least {MIN_SERVICE_KEY_LENGTH} characters "
                "(generate: openssl rand -hex 32)"
            )
        return value

    @field_validator("agent_tools_key")
    @classmethod
    def _tools_key_long_enough(cls, value: SecretStr) -> SecretStr:
        if len(value.get_secret_value()) < MIN_TOOLS_KEY_LENGTH:
            raise ValueError(
                f"AgentTools__Key must be at least {MIN_TOOLS_KEY_LENGTH} characters "
                "(generate: openssl rand -hex 32)"
            )
        return value

    @model_validator(mode="after")
    def _keys_differ(self) -> "Settings":
        # Same rule as scripts/dev-secrets.sh: one leaked key must not open both directions.
        if self.service_key.get_secret_value() == self.agent_tools_key.get_secret_value():
            raise ValueError("AgentService__ServiceKey and AgentTools__Key must be different")
        return self


@lru_cache
def get_settings() -> Settings:
    return Settings()
