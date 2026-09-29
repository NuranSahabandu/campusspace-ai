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
LLM_TIMEOUT_S = 60
LLM_MAX_RETRIES = 3

# Supervisor planner: invalid structured output is retried once (plan §10.10), then the stub plan.
MAX_PLANNER_ATTEMPTS = 2
# Wall clock for the whole planner step. The client alone can take ~4 min (60 s × (1 + 3 retries)),
# which would outlast RUN_TIMEOUT_S and .NET's watchdog, so the planner waits at most this long.
PLANNER_DEADLINE_S = 60.0
PLANNER_MIN_RETRY_S = 10.0  # don't start the retry attempt with less than this left
