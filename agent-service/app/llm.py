"""Gemini chat models with the Labs 05–07 client settings, and token usage bookkeeping.

build_chat_model is called only when an LLM agent first needs a model (never at import time), so
stub mode (the default, and CI) never touches Gemini or the key. The key is passed from Settings
explicitly: pydantic-settings reads the repo-root .env without exporting it to os.environ.
"""

import json
from functools import lru_cache
from typing import Any, Literal

from app.config import Settings
from app.faults import FaultTransport
from app.limits import LLM_MAX_RETRIES, LLM_TIMEOUT_S

Role = Literal["planner", "worker"]

CHARS_PER_TOKEN = 4  # Lab 07's estimate, used only when a response has no usage_metadata


@lru_cache(maxsize=1)
def _gemini_class() -> type:
    """ChatGoogleGenerativeAI with google-genai's automatic function calling (AFC) turned off.

    AFC is the SDK's own tool loop: it runs Python callables passed as tools. We never pass any
    (create_agent's ToolNode runs our tools, and Gemini only sees declarations), so AFC can never
    act. Yet without disable=True every generate_content call logs "AFC is enabled with max remote
    calls: 10" (INFO) and the first one also warns "Direct use of automatic function calling (AFC)
    ... is not recommended" (WARNING). Turning it off makes the SDK take its no-AFC branch, so
    neither line is emitted; no other log is touched. langchain-google-genai forwards extra request
    kwargs into GenerateContentConfig but not model_kwargs, hence the _prepare_request override.
    """
    from google.genai.types import AutomaticFunctionCallingConfig
    from langchain_google_genai import ChatGoogleGenerativeAI

    class CampusGeminiChat(ChatGoogleGenerativeAI):
        def _prepare_request(self, messages: Any, **kwargs: Any) -> dict[str, Any]:
            kwargs.setdefault(
                "automatic_function_calling", AutomaticFunctionCallingConfig(disable=True)
            )
            return super()._prepare_request(messages, **kwargs)

    return CampusGeminiChat


def build_chat_model(role: Role, settings: Settings, agent: str | None = None) -> Any:
    """agent names the caller ("supervisor", "venue_matching", ...) so a development fault
    (app/faults.py) can be attached to that agent's client only."""
    chat_class = _gemini_class()

    if settings.google_api_key is None:
        raise RuntimeError("GOOGLE_API_KEY is not configured")
    planner = role == "planner"
    extra: dict[str, Any] = {}
    fault = settings.fault_for(agent) if agent else None
    if fault:
        extra["client_args"] = {
            "transport": FaultTransport(fault, agent or role, settings.agent_fault_times)
        }
    return chat_class(
        model=settings.planner_model if planner else settings.worker_model,
        google_api_key=settings.google_api_key,
        temperature=0,
        timeout=LLM_TIMEOUT_S,
        max_retries=LLM_MAX_RETRIES,  # the free tier is metered per minute; back off and retry
        # Gemini 3 thinking cap (thinking_budget is deprecated for 3.x); thoughts are not returned.
        thinking_level=settings.planner_thinking if planner else settings.worker_thinking,
        **extra,
    )


def _chain(exc: BaseException) -> list[BaseException]:
    seen: list[BaseException] = []
    current: BaseException | None = exc
    while current is not None and current not in seen:
        seen.append(current)
        current = current.__cause__ or current.__context__
    return seen


def _http_reason(code: int, details: Any) -> str:
    if code == 429:
        return "rate limited (HTTP 429)"
    if code == 400:
        if "API_KEY_INVALID" in json.dumps(details, default=str):
            return "API key rejected (HTTP 400)"
        return "invalid request (HTTP 400)"
    if code in (401, 403):
        return f"not authorised (HTTP {code})"
    if code == 404:
        return "model not found (HTTP 404)"
    if code == 408:
        return "timeout (HTTP 408)"
    if code >= 500:
        return f"server error (HTTP {code})"
    return f"HTTP {code}"


def llm_error_reason(exc: BaseException) -> str:
    """A fixed text for a failed model call (Task 5.5 drills). Never the provider's message or body
    (langchain-google-genai's str() carries Google's whole error JSON) and never an exception
    message, which can quote model output: it reaches the officer's trace and the logs."""
    import httpx
    from google.genai import errors

    for error in _chain(exc):
        if isinstance(error, errors.APIError) and isinstance(error.code, int):
            return _http_reason(error.code, error.details)
        if isinstance(error, httpx.TimeoutException | TimeoutError):
            return "timeout"
        if isinstance(error, httpx.TransportError):
            return "connection error"
    return type(exc).__name__


def usage_from(message: Any, prompt_chars: int, output_chars: int) -> dict[str, Any]:
    """Token counts of one call from usage_metadata (Lab 05: the `or {}` guard matters, usage is not
    on every response). Missing counts are estimated at ~4 characters per token and marked."""
    meta = getattr(message, "usage_metadata", None) or {}
    if meta.get("input_tokens") is not None and meta.get("output_tokens") is not None:
        inp, out, estimated = int(meta["input_tokens"]), int(meta["output_tokens"]), False
    else:
        inp, out = prompt_chars // CHARS_PER_TOKEN, output_chars // CHARS_PER_TOKEN
        estimated = True
    return {
        "input_tokens": inp,
        "output_tokens": out,
        "total_tokens": inp + out,
        "llm_calls": 1,
        "estimated": estimated,
    }


def add_usage(total: dict[str, Any] | None, more: dict[str, Any] | None) -> dict[str, Any] | None:
    if more is None:
        return total
    if total is None:
        return dict(more)
    return {
        "input_tokens": total["input_tokens"] + more["input_tokens"],
        "output_tokens": total["output_tokens"] + more["output_tokens"],
        "total_tokens": total["total_tokens"] + more["total_tokens"],
        "llm_calls": total["llm_calls"] + more.get("llm_calls", 1),
        "estimated": bool(total["estimated"] or more["estimated"]),
    }
