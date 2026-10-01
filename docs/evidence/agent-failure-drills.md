# Agent failure drills with live Gemini (Task 5.5)

Evidence for the report's agent evaluation (plan §10.10 recovery flow) and security (§10.13, §16) sections.
Run on **2026-10-01** against the Development API (poller on), PostgreSQL 16 and the agent service with all four
LLM agents on (`supervisor, venue_matching, equipment_allocation, policy_cost`; planner `gemini-3.5-flash`,
workers `gemini-3.5-flash-lite`, thinking low/minimal). The file holds no secrets, request notes or ids beyond
drill numbers.

## Method

- **Where the fault is injected.** It goes in at the lowest seam, an httpx transport inside google-genai's client
  (`agent-service/app/faults.py`, `AGENT_FAULT` / `AGENT_FAULT_AGENTS` / `AGENT_FAULT_TIMES`, development only). So the
  real SDK error mapping and retry, langchain's parsing, `create_agent`, our retries, the step deadlines, the shared
  run budget and the stub fallbacks all run unchanged.
- **Which faults reach Google.** `rate_limit*`, `slow`, `connection` and `malformed` never reach Google. `bad_key` and
  `model_not_found` send the real request with a broken key or model name, so Gemini answers with its real
  (unbilled) rejection.
- **The request.** Every drill submits the same request: a Student with a club, 40 attendees, 10:00–12:00 campus time
  on a weekday 26 Oct–12 Nov 2026, the `projector` feature, 2 × MIC-WIRELESS and 1 × PROJ-PORTABLE (built into the
  room), budget LKR 20,000. A requester under the open-request cap is chosen before every submit. The request is
  cancelled by its owner (which sends no email) once its data is recorded.
- **What the officer and the requester see.** These come from the API responses that the screens render:
  - `GET /api/agent-runs/{id}` (React approval detail and run detail)
  - `GET /api/agent-runs?requestId=` (the Agent runs monitor row: `totalTokens`, `anyFallback`, `failureReason`)
  - `GET /api/booking-requests/{id}` as the requester (Flutter `RequestOutcomeCard`)
- **Leak scan.** For each drill, this scans the trace (run detail and monitor row), the requester's and officer's
  request views, and the agent-service log. The patterns:
  - the Google key shape `AIza[0-9A-Za-z_-]{35}` (`.env` is never read)
  - a notes sentinel, in the trace and the log only, because the owner and the officer legitimately see notes
  - raw Google error text (`{'error'`, `RESOURCE_EXHAUSTED`, `API_KEY_INVALID`, `INVALID_ARGUMENT`, `NOT_FOUND`,
    `generativelanguage`)
  - `Traceback`
  - key headers with a value
- **Time and tokens.**
  - Run time is Σ step `durationMs` (the 5.1 "agent processing time").
  - The LLM budget is 150 s (`RUN_TIMEOUT_S` 180 − 30 s reserve), and the .NET watchdog is 4 min.
  - Tokens are the run's `totalTokens` from the monitor ("—" means no usage, so no billable call).
- **Scripts.** `/tmp/checklist-5.5/` (`common.sh`, `drill.sh`, `d10.sh`, `d11_12.sh`, `summarize.py`, `leakscan.sh`).

## Results

The pass/fail verdict compares the actual result with the expected one. Every row also checks that nothing leaked.
D0–D12 ran on the code *before* the two fixes below, and D13 re-runs D2 and D6 after them.

| # | Fault (agents) | Expected (from the code) | Actual | Pass | Run time | Tokens |
|---|---|---|---|---|---|---|
| D0 | none | AwaitingApproval, all four agents llm, no fallback | AwaitingApproval; all llm; 12/12 rules; LKR 4,000.00. AFC INFO line + one-time AFC WARNING on the planner call | ✅ | 12.4 s | 9,763 |
| D1 | `rate_limit` ×2 (supervisor) | the SDK retry absorbs it; planner stays llm | 2 × 429 retried after 1.9 s and 2.0 s; planner llm on attempt 1; no fallback | ✅ | 17.2 s | 9,848 |
| D2 | `rate_limit` storm (equipment) | 3 calls, then equipment falls back; others llm; AwaitingApproval | 3 calls (attempts=3), fallback in 4.1 s; AwaitingApproval. **Bug: the reason quoted Google's raw error JSON** (fixed, see D13a) | ✅ (bug found) | 13.9 s | 7,701 |
| D3 | `rate_limit_retry_after` 30 s (policy) | Retry-After ignored; policy falls back to the template summary | the header was ignored (same 1–2 s backoff); fallback in 3.7 s; template officer summary; raw JSON in the reason (same bug) | ✅ (bug found) | 13.3 s | 6,894 |
| D4 | `slow` 75 s (all four) | planner 60 s + venue 45 s + equipment 45 s ≈ 150 s, policy skipped "run time budget exhausted"; AwaitingApproval < 180 s | planner 60.0 s, venue 45.0 s, equipment 45.0 s, policy skipped "run time budget exhausted"; AwaitingApproval after 151 s, well inside 180 s and the 4 min watchdog; stub plan; valid proposal (E201) at the same price | ✅ | 150.0 s | — |
| D5 | `bad_key` (venue) | real 400 API_KEY_INVALID, not retried; venue falls back; no key anywhere | real 400, 1 call; venue fallback; key pattern 0 hits; raw JSON in the reason (same bug) | ✅ (bug found) | 10.6 s | 6,629 |
| D6 | `model_not_found` (supervisor) | real 404, planner falls back to the stub plan | real 404, 1 call; `planner_fallback: true`; workers llm; AwaitingApproval; raw JSON in the reason (same bug) | ✅ (bug found) | 9.8 s | 8,517 |
| D7 | `malformed` (supervisor) | not a Plan → 1 retry → "Planner output invalid twice" | 2 calls, then fallback "Planner output invalid twice". **Bug: the reason quoted the model's completion** (fixed in the same commit; unit test) | ✅ (bug found) | 9.9 s | 10,105 |
| D8 | `malformed` (venue) | schema error → retry → "Venue output invalid twice" | 2 calls, then "Venue output invalid twice: VenueResult options: Field required"; venue stub | ✅ | 10.7 s | 7,519 |
| D9 | `connection` (equipment) | ConnectError retried by the SDK, then equipment falls back | 3 calls (1.6 s + 2.8 s backoff), fallback in 4.5 s; AwaitingApproval | ✅ | 15.3 s | 7,584 |
| D10 | agent service killed 20 s into a run, kept down | poller sees "network error"; the watchdog fails the run after 4 min; AgentFailed; Retry agent works after a restart | run Failed "Agent run timed out (last error: network error)" after 242.8 s; request AgentFailed, and the requester sees "We couldn't prepare a proposal: Agent run timed out (last error: network error). Facilities can retry."; after a restart, Retry agent → 202, RevisionNo 2 AwaitingApproval (all llm, 14.5 s) | ✅ | 14.5 s (retry) | 9,989 (retry) |
| D11 | agent service restarted 20 s into a run | GET → failed "Agent service restarted during the run" → AgentFailed | Failed "Agent service restarted during the run" 3 s after the restart; AgentFailed. (The owner can't cancel AgentFailed; the drill retried it and then cancelled it) | ✅ | — | — |
| D12 | restart while AwaitingApproval (D2's run), then approve | the SqliteSaver checkpoint survives; resume → finalize → Approved | approve → 200 Approved; run Completed, nodes end `human_gate, finalize`; the requester sees Approved, LKR 4,000.00 | ✅ | — | 0 (finalize is code) |
| D13a | D2 again, after the fixes | reason is a fixed text; no raw JSON in the trace; 0 AFC lines | "Equipment LLM error: rate limited (HTTP 429)"; raw text in the trace 0; AFC lines 0 | ✅ | 15.4 s | 7,732 |
| D13b | D6 again, after the fixes | the same | "Planner LLM error: model not found (HTTP 404)"; AFC lines 0 | ✅ | 9.7 s | 8,228 |

**Leaks over all drills.**
- Google key pattern: 0 hits.
- Notes sentinel in the trace or the logs: 0.
- Key headers with values: 0.
- Tracebacks: only the deliberate startup refusal of a mis-set fault.

**What the officer and the requester see.**
- **The officer, in React.**
  - **AgentTimeline:** each fallback step shows `mode: fallback` with `fallback_reason`.
  - **Agent runs monitor:** the run row is flagged `anyFallback` and matches the `fallback` filter.
  - **Failed runs:** the run shows the failure reason as plain text, and the request detail offers "Retry agent".
- **The requester, in Flutter.** The requester never sees fallbacks. They see the same "Proposed: <room>, LKR 4,000.00 / Waiting for the Facilities Officer" in every drill that reached the gate, and the AgentFailed text above in D10 and D11.
- **Price.** Every proposal was priced by .NET (`IQuotationCalculator`), so no fault changed a price.

**Live runs.** 16 of the 25 allowed: D0–D9, D10 (2), D11, D13a and D13b, plus one unplanned stub-intended cleanup retry that ran with the LLM agents on. D4, D10's first run and D11 made no billable call.

## Fixes made from the drills

1. **`fix(agent): fixed-text LLM error reasons`** (D2, D3, D5, D6, D7). `fallback_reason` and the warning logs carried
   `str(exc)` from langchain-google-genai, which is Google's whole error JSON. The planner's parse error also quoted
   the model's completion. Now `app/llm.py` `llm_error_reason()` maps the error chain to fixed texts:
   - "rate limited (HTTP 429)"
   - "API key rejected (HTTP 400)"
   - "invalid request (HTTP 400)"
   - "not authorised (HTTP 401/403)"
   - "model not found (HTTP 404)"
   - "timeout"
   - "connection error"
   - "server error (HTTP 5xx)"
   - "HTTP n"
   - otherwise only the exception type

   Schema problems name only the field.
2. **`fix(agent): disable Gemini automatic function calling`** (D0). See the AFC note below.
3. **`fix(agent): round timeout reasons …`** (D4: "44.9503 s"). The commit also corrects the `limits.py` comment:
   `max_retries=3` is google-genai `HttpRetryOptions(attempts=3)`, which is three calls in all, not 1 + 3.

## Found and not fixed (by choice)

- **google-genai ignores `Retry-After` (D3).** Its tenacity backoff is about 1 s, then 2 s, whatever the header says.
  The step deadline already bounds the wait. Honouring a long Retry-After would only spend the run budget, and the
  fallback is the intended safe behaviour.
- **google-genai's own INFO retry log** ("Retrying … as it raised ClientError: 429 RESOURCE_EXHAUSTED. {…}") contains
  Google's error body. It is a third-party INFO line, hidden at the default log level (it showed only because the
  drills ran with root INFO). It contains no key and no notes, and it never reaches the trace or the UI.
- **Abandoned slow calls keep sleeping (D4).** A timed-out call's daemon thread keeps going, and the SDK even retries
  the timeout inside it. The step ignores its late result, and the thread holds no lock and dies with the process.
- **AgentFailed can't be cancelled by its owner (D11).** This matches the state machine. Facilities can retry it.
- **Other third-party warnings left visible on purpose.** "Key 'additionalProperties' is not supported in schema"
  (langchain-google-genai) and "Model 'gemini-3.5-flash-lite' uses fixed sampling defaults" (Flash-Lite ignores
  temperature, a known limitation from Task 4.3).

## AFC

- **What emits it.** google-genai `Models.generate_content` logs "AFC is enabled with max remote calls: 10" (INFO,
  every call) and, once per process, "Direct use of automatic function calling (AFC) in Models.generate_content is
  not recommended" (WARNING). It does this whenever `GenerateContentConfig.automatic_function_calling.disable` is not
  true.
- **Why it is noise.** AFC is the SDK's own tool loop over Python callables. We only send function declarations, and
  our `create_agent` loop runs the tools, so AFC could never act.
- **The fix.** `CampusGeminiChat` (`app/llm.py`) sets `automatic_function_calling.disable=True` on every request. The
  SDK then takes its no-AFC branch and logs neither line. There is no log filter, and other warnings are untouched.
- **Verified.**
  - Unit tests: the real client offline logs neither line, while the plain client does (the contrast test).
  - Live: 0 AFC lines in D13a and D13b, against 2–3 per run before the fix.
