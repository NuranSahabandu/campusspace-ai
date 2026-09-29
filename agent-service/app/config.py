"""Settings from the repo-root .env and real environment variables (env vars win).

Variable names match the .NET API's (AgentService__ServiceKey etc.), so one .env serves both.
"""

from functools import lru_cache
from pathlib import Path
from typing import Literal

from pydantic import Field, SecretStr, ValidationInfo, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

# Anchored to this file (app/config.py -> agent-service/ -> repo root), not the working directory.
ROOT_ENV = Path(__file__).resolve().parents[2] / ".env"
# agent-service/data/ is git-ignored; anchored to this file like ROOT_ENV.
DEFAULT_CHECKPOINT_PATH = Path(__file__).resolve().parents[1] / "data" / "checkpoints.sqlite"

MIN_SERVICE_KEY_LENGTH = 32
MIN_TOOLS_KEY_LENGTH = 32

# Agents that AGENT_LLM_AGENTS can switch to Gemini, and the ones that have an LLM implementation.
# Phase 4 adds each worker to LLM_IMPLEMENTED as it lands (4.2 venue, 4.3 equipment, 4.4 policy).
LLM_AGENTS = ("supervisor", "venue_matching", "equipment_allocation", "policy_cost")
WORKER_AGENTS = LLM_AGENTS[1:]
LLM_IMPLEMENTED = frozenset({"supervisor", "venue_matching", "equipment_allocation"})
_PENDING_TASK = {"venue_matching": "4.2", "equipment_allocation": "4.3", "policy_cost": "4.4"}

# Same Flash (planning) / Flash-Lite (workers) split as the labs (Labs 06/07 and Lab 05 api/main.py
# CHAT_MODEL), one generation newer: Gemini answers 404 "no longer available to new users" for the
# labs' gemini-2.5-flash and gemini-2.5-flash-lite (checked 2026-09-29).
DEFAULT_PLANNER_MODEL = "gemini-3.5-flash"
DEFAULT_WORKER_MODEL = "gemini-3.5-flash-lite"
# Gemini 3 thinking levels (langchain-google-genai `thinking_level`; the 3.5 profiles list all 4).
# Low defaults: the planner fills one small schema, the workers pick from tool results.
# gemini-3.5-flash's own default is "medium"; -lite's is already "minimal".
ThinkingLevel = Literal["minimal", "low", "medium", "high"]
DEFAULT_PLANNER_THINKING = "low"
DEFAULT_WORKER_THINKING = "minimal"
MAX_MODEL_LABEL = 100  # AgentRuns.Model column


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

    # Comma list of agents that call Gemini. Empty (the default, and CI) means all stubs.
    agent_llm_agents: str = Field(default="", validation_alias="AGENT_LLM_AGENTS")
    planner_model: str = Field(default=DEFAULT_PLANNER_MODEL, validation_alias="PLANNER_MODEL")
    worker_model: str = Field(default=DEFAULT_WORKER_MODEL, validation_alias="WORKER_MODEL")
    planner_thinking: ThinkingLevel = Field(
        default=DEFAULT_PLANNER_THINKING, validation_alias="PLANNER_THINKING"
    )
    worker_thinking: ThinkingLevel = Field(
        default=DEFAULT_WORKER_THINKING, validation_alias="WORKER_THINKING"
    )

    @field_validator("checkpoint_path", mode="before")
    @classmethod
    def _empty_path_means_default(cls, value: object) -> object:
        # .env.example lists AGENT_CHECKPOINT_PATH= empty; that must not become Path(".").
        return DEFAULT_CHECKPOINT_PATH if value in (None, "") else value

    @field_validator("planner_model", "worker_model", mode="before")
    @classmethod
    def _empty_model_means_default(cls, value: object, info: ValidationInfo) -> object:
        if value not in (None, ""):
            return value
        return {"planner_model": DEFAULT_PLANNER_MODEL}.get(info.field_name, DEFAULT_WORKER_MODEL)

    @field_validator("planner_thinking", "worker_thinking", mode="before")
    @classmethod
    def _empty_thinking_means_default(cls, value: object, info: ValidationInfo) -> object:
        if isinstance(value, str) and value.strip():
            return value.strip().lower()
        if value not in (None, ""):
            return value
        return {"planner_thinking": DEFAULT_PLANNER_THINKING}.get(
            info.field_name, DEFAULT_WORKER_THINKING
        )

    @field_validator("google_api_key", mode="before")
    @classmethod
    def _empty_key_means_missing(cls, value: object) -> object:
        # .env.example lists GOOGLE_API_KEY= empty; that is "not configured", not a key.
        if value is None or (isinstance(value, str) and not value.strip()):
            return None
        return value

    @field_validator("agent_llm_agents")
    @classmethod
    def _known_llm_agents(cls, value: str) -> str:
        names = [n.strip().lower() for n in value.split(",") if n.strip()]
        unknown = [n for n in names if n not in LLM_AGENTS]
        if unknown:
            raise ValueError(
                f"AGENT_LLM_AGENTS has unknown agent(s) {', '.join(unknown)}; "
                f"allowed: {', '.join(LLM_AGENTS)}"
            )
        pending = [n for n in names if n not in LLM_IMPLEMENTED]
        if pending:
            # /health must never claim "llm" for an agent that is still a stub.
            missing = [
                f"{n} has no LLM implementation yet (Task {_PENDING_TASK[n]})" for n in pending
            ]
            raise ValueError("AGENT_LLM_AGENTS: " + "; ".join(missing))
        return ",".join(n for n in LLM_AGENTS if n in names)

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

    @model_validator(mode="after")
    def _llm_needs_key(self) -> "Settings":
        if self.llm_agents and self.google_api_key is None:
            raise ValueError(
                "GOOGLE_API_KEY is required when AGENT_LLM_AGENTS enables an LLM agent "
                f"({', '.join(sorted(self.llm_agents))}); set it in the repo-root .env, or leave "
                "AGENT_LLM_AGENTS empty for stub agents"
            )
        return self

    @property
    def llm_agents(self) -> frozenset[str]:
        return frozenset(n for n in self.agent_llm_agents.split(",") if n)

    def agent_mode(self, name: str) -> str:
        return "llm" if name in self.llm_agents else "stub"

    def model_label(self) -> str:
        """AgentRuns.Model: the model id each agent really uses, for example
        "planner=gemini-3.5-flash; workers=stub" or
        "planner=stub; venue_matching=gemini-3.5-flash-lite, others=stub". LLM workers that share a
        model are joined with "+" ("venue_matching+equipment_allocation=<id>")."""
        planner = self.planner_model if self.agent_mode("supervisor") == "llm" else "stub"
        workers = {w: self.worker_model if self.agent_mode(w) == "llm" else "stub"
                   for w in WORKER_AGENTS}  # fmt: skip
        if len(set(workers.values())) == 1:
            worker_part = f"workers={next(iter(workers.values()))}"
        else:
            # Only the LLM workers by name, so the label fits the 100-char column.
            by_model: dict[str, list[str]] = {}
            for w, m in workers.items():
                if m != "stub":
                    by_model.setdefault(m, []).append(w)
            llm = [f"{'+'.join(names)}={m}" for m, names in by_model.items()]
            worker_part = ", ".join(llm) + ", others=stub"
        return f"planner={planner}; {worker_part}"[:MAX_MODEL_LABEL]


@lru_cache
def get_settings() -> Settings:
    return Settings()
