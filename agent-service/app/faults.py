"""Development-only LLM fault injection (Task 5.5 failure drills).

The fault sits at the lowest seam, the httpx transport inside google-genai's client (passed through
ChatGoogleGenerativeAI's client_args), so everything above it runs unchanged: google-genai's error
mapping and its tenacity retry, langchain's parsing, create_agent, our retry, the step deadlines,
the run budget and the stub fallbacks.

Off by default. Settings refuses AGENT_FAULT unless AGENT_ENV=development, refuses unknown faults,
and refuses a target agent that is not an LLM agent (the fault would be silently inert). CI never
sets it (tests/test_faults.py guards ci.yml). Only the fault name, the agent and a call number are
ever logged.
"""

import json
import logging
import re
import threading
import time
from collections.abc import Callable
from typing import Any

import httpx

log = logging.getLogger("agent_service.faults")

# rate_limit*, slow, connection and malformed never reach Google; bad_key and model_not_found send
# the real request with a broken key or model so Gemini answers with its real rejection (unbilled).
FAULTS = (
    "rate_limit",
    "rate_limit_retry_after",
    "slow",
    "connection",
    "malformed",
    "bad_key",
    "model_not_found",
)
SLOW_S = 75.0  # beyond LLM_TIMEOUT_S (60) and every step deadline (planner 60, workers 45)
RETRY_AFTER_S = "30"
INVALID_KEY = "fault-injection-invalid-key"
MISSING_MODEL = "gemini-0-nonexistent"
# Structured-output tools that create_agent's ToolStrategy declares (the schema class names).
STRUCTURED_TOOLS = ("VenueResult", "EquipmentResult", "PolicyAnswer")


def _rate_limited(request: httpx.Request, retry_after: bool) -> httpx.Response:
    headers = {"Retry-After": RETRY_AFTER_S} if retry_after else {}
    body = {"error": {"code": 429, "status": "RESOURCE_EXHAUSTED",
                      "message": "Resource exhausted (fault injection)."}}  # fmt: skip
    return httpx.Response(429, json=body, headers=headers, request=request)


def _malformed(request: httpx.Request) -> httpx.Response:
    """A well-formed Gemini 200 whose content fails our schema: a function call to the
    structured-output tool with wrong args (workers), or JSON text that is not a Plan (planner)."""
    body = json.loads(request.content or b"{}")
    declared = [
        d.get("name")
        for tool in body.get("tools") or []
        for d in tool.get("functionDeclarations") or []
    ]
    structured = next((n for n in declared if n in STRUCTURED_TOOLS), None)
    if structured is not None:
        part: dict[str, Any] = {"functionCall": {"name": structured, "args": {"bogus": 1}}}
    else:
        part = {"text": json.dumps({"steps": "not-a-list", "bogus": 1})}
    return httpx.Response(
        200,
        json={
            "candidates": [{"content": {"role": "model", "parts": [part]}, "finishReason": "STOP"}],
            "usageMetadata": {
                "promptTokenCount": 0,
                "candidatesTokenCount": 0,
                "totalTokenCount": 0,
            },  # fmt: skip
        },
        request=request,
    )


class FaultTransport(httpx.BaseTransport):
    """Wraps the real transport for ONE agent's Gemini client. times=0 faults every call, N only
    the first N (later calls pass through, so a transient fault can be absorbed by the retry)."""

    def __init__(
        self,
        fault: str,
        agent: str,
        times: int = 0,
        inner: httpx.BaseTransport | None = None,
        sleep: Callable[[float], None] = time.sleep,
    ) -> None:
        if fault not in FAULTS:
            raise ValueError(f"unknown fault {fault!r}")
        self.fault, self.agent, self.times = fault, agent, times
        self._inner = inner or httpx.HTTPTransport()
        self._sleep = sleep
        self._lock = threading.Lock()
        self.calls = 0
        self.injected = 0

    def handle_request(self, request: httpx.Request) -> httpx.Response:
        with self._lock:
            self.calls += 1
            inject = self.times == 0 or self.calls <= self.times
            if inject:
                self.injected += 1
            n = self.calls
        if not inject:
            return self._inner.handle_request(request)
        log.warning("fault injection: %s on %s (call %d)", self.fault, self.agent, n)
        match self.fault:
            case "rate_limit" | "rate_limit_retry_after":
                return _rate_limited(request, self.fault == "rate_limit_retry_after")
            case "slow":
                self._sleep(SLOW_S)
                raise httpx.ReadTimeout("fault injection: slow call", request=request)
            case "connection":
                raise httpx.ConnectError("fault injection: connection refused", request=request)
            case "malformed":
                return _malformed(request)
            case "bad_key":
                request.headers["x-goog-api-key"] = INVALID_KEY
                return self._inner.handle_request(request)
            case _:  # model_not_found
                path = re.sub(r"/models/[^/:]+", f"/models/{MISSING_MODEL}", request.url.path)
                return self._inner.handle_request(_with_path(request, path))

    def close(self) -> None:
        self._inner.close()


def _with_path(request: httpx.Request, path: str) -> httpx.Request:
    return httpx.Request(
        request.method,
        request.url.copy_with(path=path),
        headers=request.headers,
        content=request.content,
        extensions=request.extensions,
    )
