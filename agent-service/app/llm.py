"""Gemini chat models with the Labs 05–07 client settings, and token usage bookkeeping.

build_chat_model is called only when an LLM agent first needs a model (never at import time), so
stub mode (the default, and CI) never touches Gemini or the key. The key is passed from Settings
explicitly: pydantic-settings reads the repo-root .env without exporting it to os.environ.
"""

from typing import Any, Literal

from app.config import Settings
from app.limits import LLM_MAX_RETRIES, LLM_TIMEOUT_S

Role = Literal["planner", "worker"]

CHARS_PER_TOKEN = 4  # Lab 07's estimate, used only when a response has no usage_metadata


def build_chat_model(role: Role, settings: Settings) -> Any:
    from langchain_google_genai import ChatGoogleGenerativeAI

    if settings.google_api_key is None:
        raise RuntimeError("GOOGLE_API_KEY is not configured")
    return ChatGoogleGenerativeAI(
        model=settings.planner_model if role == "planner" else settings.worker_model,
        google_api_key=settings.google_api_key,
        temperature=0,
        timeout=LLM_TIMEOUT_S,
        max_retries=LLM_MAX_RETRIES,  # the free tier is metered per minute; back off and retry
    )


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
