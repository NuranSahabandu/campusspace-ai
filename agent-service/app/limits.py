"""Hard runtime limits (plan §10.10). Code constants, not policy (addendum Open question 5).

MAX_DELEGATIONS deviates from plan §10.5's 6. A full plan is WORKERS_PER_PLAN worker calls, so 6
would always stop the second re-plan before MAX_REPLANS applied. It is derived here so the two
limits can never collide. It counts per revision (an officer revise starts a fresh count), so only a
runaway (for example a routing bug that re-dispatches a worker) can reach it.
"""

WORKERS_PER_PLAN = 3  # venue_matching, equipment_allocation, policy_cost
MAX_REPLANS = 2
MAX_DELEGATIONS = WORKERS_PER_PLAN * (1 + MAX_REPLANS)

# Supersteps per graph invocation. Worst case (a revise followed by two re-plans) is
# 1 (human_gate) + 3 × 8 (supervisor + 3 × (worker + supervisor) + validate) + 1 (final node) = 26.
GRAPH_RECURSION_LIMIT = 40
WORKER_RECURSION_LIMIT = 12  # Phase 4 LLM workers (plan §10.6)

TOOL_TIMEOUT_S = 10.0
RUN_TIMEOUT_S = 180.0  # per run segment (start, or one resume); the officer's wait does not count

MAX_NOTES_LENGTH = 1000  # officer revise notes

# LLM client (Labs 05–07 get_llm(): temperature 0, timeout 60, max_retries 3 for free-tier 429s).
# langchain-google-genai passes max_retries to google-genai as HttpRetryOptions(attempts=3): three
# calls in all (two retries, ~1 s then ~2 s backoff with jitter) for 408/429/5xx and connection or
# timeout errors; Retry-After is not read (Task 5.5 drills D1-D3, D9).
LLM_TIMEOUT_S = 60
LLM_MAX_RETRIES = 3

# LLM steps: invalid structured output is retried once (plan §10.10), then the stub.
MAX_PLANNER_ATTEMPTS = 2
MAX_WORKER_ATTEMPTS = 2
# Wall clock per LLM step. The client alone can take ~3 min (60 s × 3 attempts + backoff), which
# would outlast RUN_TIMEOUT_S and .NET's watchdog, so a step waits at most this long.
PLANNER_DEADLINE_S = 60.0
WORKER_DEADLINE_S = 45.0
# Shared per-segment LLM budget (app/budget.py): an LLM step gets
# min(its deadline, time left in the segment - LLM_RESERVE_S), and is skipped (stub) below
# LLM_MIN_BUDGET_S. So every LLM wait ends by RUN_TIMEOUT_S - LLM_RESERVE_S (150 s), whatever the
# number of re-plans, and the reserve is left for the stubs, validate and their tool calls.
LLM_RESERVE_S = 30.0
LLM_MIN_BUDGET_S = 10.0  # also: don't start a retry attempt with less than this left
