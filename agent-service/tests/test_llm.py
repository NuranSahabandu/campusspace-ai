"""The Gemini client factory (Labs 05–07 settings) and token bookkeeping. No model call."""

import pytest
from langchain_core.messages import AIMessage

from app.llm import add_usage, build_chat_model, usage_from
from tests.conftest import make_settings

FAKE_GOOGLE_KEY = "fake-google-key-" + "g" * 24


def test_build_chat_model_uses_the_lab_settings(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = make_settings(
        monkeypatch, AGENT_LLM_AGENTS="supervisor", GOOGLE_API_KEY=FAKE_GOOGLE_KEY
    )

    planner = build_chat_model("planner", settings)
    worker = build_chat_model("worker", settings)

    assert planner.model == "gemini-3.5-flash"
    assert worker.model == "gemini-3.5-flash-lite"
    assert (planner.temperature, planner.timeout, planner.max_retries) == (0, 60, 3)
    assert FAKE_GOOGLE_KEY not in repr(planner)


def test_build_chat_model_without_a_key_raises(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(RuntimeError, match="GOOGLE_API_KEY is not configured"):
        build_chat_model("planner", make_settings(monkeypatch))


def test_usage_comes_from_usage_metadata() -> None:
    message = AIMessage(
        content="{}",
        usage_metadata={"input_tokens": 120, "output_tokens": 30, "total_tokens": 150},
    )

    assert usage_from(message, prompt_chars=9999, output_chars=9999) == {
        "input_tokens": 120,
        "output_tokens": 30,
        "total_tokens": 150,
        "llm_calls": 1,
        "estimated": False,
    }


def test_missing_usage_is_estimated_at_four_chars_per_token() -> None:
    assert usage_from(AIMessage(content="x"), prompt_chars=400, output_chars=81) == {
        "input_tokens": 100,
        "output_tokens": 20,
        "total_tokens": 120,
        "llm_calls": 1,
        "estimated": True,
    }
    assert usage_from(None, prompt_chars=8, output_chars=0)["estimated"] is True


def test_add_usage_sums_calls_and_keeps_the_estimated_flag() -> None:
    exact = {"input_tokens": 10, "output_tokens": 5, "total_tokens": 15, "llm_calls": 1,
             "estimated": False}  # fmt: skip
    guess = exact | {"estimated": True}

    total = add_usage(add_usage(None, exact), guess)

    assert total == {
        "input_tokens": 20,
        "output_tokens": 10,
        "total_tokens": 30,
        "llm_calls": 2,
        "estimated": True,
    }
    assert add_usage(total, None) == total
