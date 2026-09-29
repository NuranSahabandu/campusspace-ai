"""The LangGraph workflow (plan §10.2, Appendix A.1): a hub-and-spoke supervisor, a deterministic
validate node, and an interrupt() human gate.

Everything the poller needs (nodes, steps with tool calls, validation attempts, plan, proposal) is
in append-only state keys, so it lives in the checkpoint and survives a restart. Numbering never
resets within a thread: step sequence is len(steps) + 1 and validation attempts only grow, so one
AgentRuns row per thread stays unique on (RunId, Sequence) and (RunId, Attempt, RuleCode) across
revisions.
"""

import operator
import time
from collections.abc import Callable, Mapping
from datetime import UTC, datetime
from typing import Annotated, Any, TypedDict

from langchain_core.messages import AIMessage
from langchain_core.runnables import RunnableConfig
from langchain_core.tools import BaseTool
from langgraph.checkpoint.base import BaseCheckpointSaver
from langgraph.graph import END, START, StateGraph
from langgraph.graph.message import add_messages
from langgraph.types import interrupt

from app.budget import LlmBudget
from app.limits import MAX_DELEGATIONS, MAX_REPLANS
from app.schemas import RuleResult, StepTrace
from app.tools import ToolRecorder, recording
from app.validation import (
    RECOVERABLE,
    equipment_codes,
    recheck_equipment,
    recheck_room,
    v02_room_free,
    v08_from_recheck,
    validate_proposal,
)
from app.workers import WorkerFailed, WorkerRunner, WorkerUnavailable
from app.workers.planner import PlannerInput, StubPlanner
from app.workers.supervisor import (
    OFFICER_REVISION,
    SupervisorError,
    build_brief,
    enforce_plan_rules,
    load_context,
    load_policy,
)

Clock = Callable[[], datetime]
TERMINAL = ("completed", "rejected", "failed", "cancelled")


class State(TypedDict, total=False):
    # Plan §10.3
    messages: Annotated[list, add_messages]  # short worker reports, tagged with name= (Lab 07)
    request_id: int
    request: dict | None  # form fields from request-context; notes only in wrapped form
    plan: list[dict]  # Plan.steps
    plan_model: dict | None  # the whole Plan (V01 re-validates it)
    step_index: int
    requirements: dict
    venue: dict | None  # VenueResult
    equipment: dict | None  # EquipmentResult
    quote: dict | None  # PolicyResult
    replans: int  # validation re-plans in this revision
    replan_needed: bool
    replan_reason: str | None
    delegations: int  # worker calls in this revision (hard cap)
    revision_notes: str
    decision_: str
    status_: str  # running | completed | rejected | failed | cancelled
    error: str | None
    task_: str  # the brief for the next worker (Lab 07 task_)
    # Additions
    policy: dict | None  # snapshot taken at run start (addendum A.2) and again on a revise
    refresh_policy: bool  # set by a revise: the supervisor re-fetches the snapshot
    catalogs: dict | None
    revision: int  # RevisionNo: 1, then +1 per officer revise
    excluded_room_ids: list[int]
    validation_attempt: int
    validation_outcome: str
    proposal: dict | None
    started_at: str
    completed_at: str | None
    # Append-only trace
    nodes: Annotated[list[str], operator.add]
    steps: Annotated[list[dict], operator.add]
    validation: Annotated[list[dict], operator.add]
    seen_room_ids: Annotated[list[int], operator.add]
    seen_equipment_codes: Annotated[list[str], operator.add]


def iso(dt: datetime) -> str:
    return dt.astimezone(UTC).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def initial_state(request_id: int, started_at: datetime) -> dict[str, Any]:
    return {
        "request_id": request_id,
        "request": None,
        "plan": [],
        "plan_model": None,
        "step_index": 0,
        "replans": 0,
        "replan_needed": False,
        "replan_reason": None,
        "delegations": 0,
        "refresh_policy": False,
        "revision": 1,
        "excluded_room_ids": [],
        "validation_attempt": 0,
        "status_": "running",
        "error": None,
        "proposal": None,
        "started_at": iso(started_at),
        "completed_at": None,
    }


def advance_step(index: int) -> int:
    """The next plan step. A separate function so a test can inject a routing bug (runaway)."""
    return index + 1


def _step(
    state: Mapping[str, Any],
    agent: str,
    started: float,
    recorder: ToolRecorder,
    *,
    input: dict[str, Any] | None,
    output: dict[str, Any] | None,
    error: str | None = None,
) -> dict[str, Any]:
    return StepTrace(
        sequence=len(state.get("steps", [])) + 1,
        agent_name=agent,
        status="Failed" if error else "Succeeded",
        input=input,
        output=output,
        retries=recorder.retries,
        error=error,
        duration_ms=int((time.perf_counter() - started) * 1000),
        tool_calls=recorder.calls,
    ).model_dump(mode="json")


def _seen(recorder: ToolRecorder) -> dict[str, list]:
    return {
        "seen_room_ids": recorder.seen_room_ids,
        "seen_equipment_codes": recorder.seen_equipment_codes,
    }


def _chosen(state: Mapping[str, Any]) -> dict[str, Any] | None:
    options = (state.get("venue") or {}).get("options") or []
    return options[0] if options else None


def build_graph(
    checkpointer: BaseCheckpointSaver,
    tools: Mapping[str, BaseTool],
    clock: Clock,
    planner: Any = None,
    workers: Mapping[str, Any] | None = None,
    monotonic: Callable[[], float] = time.monotonic,
):
    """planner: StubPlanner (default) or LlmPlanner (AGENT_LLM_AGENTS includes supervisor).
    workers: LLM workers that replace stubs by name (for example LlmVenueWorker). monotonic must be
    the runner's clock, because the LLM budget compares it with the segment deadline."""
    runner = WorkerRunner(tools, workers)
    planner = planner or StubPlanner()

    # ---------- supervisor (hub) ----------

    def supervisor(state: State, config: RunnableConfig) -> dict[str, Any]:
        update: dict[str, Any] = {"nodes": ["supervisor"]}
        if state.get("error"):
            return update
        view: dict[str, Any] = dict(state)
        if state.get("request") is None or state.get("replan_needed") or not state.get("plan"):
            started = time.perf_counter()
            loaded: dict[str, Any] = {}
            outcome, corrections = None, []
            with recording() as rec:
                try:
                    if state.get("request") is None:
                        loaded = load_context(tools, state["request_id"])
                    elif state.get("refresh_policy"):
                        # A revise is judged against the policy in force now.
                        loaded = {"policy": load_policy(tools)}
                    request = loaded.get("request") or state["request"]
                    catalogs = loaded.get("catalogs") or state["catalogs"]
                    outcome = planner.plan(
                        PlannerInput.from_state(state, request, catalogs),
                        LlmBudget.from_config(config, monotonic),
                    )
                    plan, corrections = enforce_plan_rules(
                        outcome.plan, request, catalogs, fallback=outcome.mode == "fallback"
                    )
                    error = None
                except SupervisorError as exc:
                    error = str(exc)
            step_input = {
                "request_id": state["request_id"],
                "revision": state.get("revision", 1),
                "replan_reason": state.get("replan_reason"),
            }
            if error:
                step = _step(state, "supervisor", started, rec, input=step_input, output=None,
                             error=error)  # fmt: skip
                return update | {"error": error, "steps": [step]}
            plan_dump = plan.model_dump(mode="json")
            step_output = plan_dump
            if outcome.mode != "stub":  # stub output stays exactly the plan (Phase 3 shape)
                step_output = plan_dump | {
                    "planner": outcome.mode,
                    "model": outcome.model,
                    "attempts": outcome.attempts,
                    "usage": outcome.usage,
                    "corrections": corrections,
                }
                if outcome.fallback_reason:
                    step_output["fallback_reason"] = outcome.fallback_reason
            step = _step(state, "supervisor", started, rec, input=step_input, output=step_output)
            update |= loaded | _seen(rec)
            update |= {
                "plan": plan_dump["steps"],
                "plan_model": plan_dump,
                "requirements": {k: v for k, v in plan_dump.items() if k != "steps"},
                "step_index": 0,
                "replan_needed": False,
                "refresh_policy": False,
                "venue": None,
                "equipment": None,
                "quote": None,
                "proposal": None,
                "steps": [step],
            }
            view |= update

        plan_steps, index = view["plan"], view["step_index"]
        if index < len(plan_steps):
            if view["delegations"] >= MAX_DELEGATIONS:
                update["error"] = (
                    f"Delegation cap reached: {MAX_DELEGATIONS} worker calls in revision "
                    f"{view.get('revision', 1)}"
                )
                return update
            update["task_"] = build_brief(plan_steps[index], view)
        return update

    def route(state: State) -> str:
        if state.get("error"):
            return "safe_failure"
        if state["step_index"] >= len(state["plan"]):
            return "validate"
        return state["plan"][state["step_index"]]["agent"]

    # ---------- workers (spokes) ----------

    def make_worker_node(name: str, state_key: str):
        def node(state: State, config: RunnableConfig) -> dict[str, Any]:
            task = state["task_"]  # the brief, never the message history
            started = time.perf_counter()
            result, output, error = None, None, None
            with recording() as rec:
                try:
                    outcome = runner.run_worker(
                        name, task, budget=LlmBudget.from_config(config, monotonic)
                    )
                    result = outcome.result
                    # Stub output stays exactly the result; an LLM worker adds mode, usage, ...
                    output = result | outcome.meta if outcome.meta else result
                except (WorkerUnavailable, WorkerFailed) as exc:
                    error = str(exc)
            step = _step(state, name, started, rec, input={"task": task}, output=output,
                         error=error)  # fmt: skip
            update: dict[str, Any] = {
                state_key: result,
                "step_index": advance_step(state["step_index"]),
                "delegations": state["delegations"] + 1,
                "steps": [step],
                "nodes": [name],
                "messages": [AIMessage(content=_report(name, output, error), name=name)],
                **_seen(rec),
            }
            if error:
                update["error"] = f"{name} failed: {error}"
            return update

        return node

    # ---------- validate (plain code) ----------

    def validate(state: State) -> dict[str, Any]:
        attempt = state.get("validation_attempt", 0) + 1
        started = time.perf_counter()
        with recording() as rec:
            results = validate_proposal(state, tools, clock())
        rows = [
            RuleResult(attempt=attempt, rule=rule, passed=passed, message=message).model_dump()
            for rule, passed, message in results
        ]
        failed = [(rule, message) for rule, passed, message in results if not passed]
        unavailable = rec.unavailable_errors()
        chosen = _chosen(state)
        update: dict[str, Any] = {
            "nodes": ["validate"],
            "validation": rows,
            "validation_attempt": attempt,
        }
        # With no room there is no quote, so V09 fails for that reason alone (not a bug signal).
        fatal = [
            f for f in failed if f[0] not in RECOVERABLE and not (chosen is None and f[0] == "V09")
        ]
        detail = "; ".join(f"{r}: {m}" for r, m in fatal + [f for f in failed if f not in fatal])
        if not failed:
            outcome = "pass"
            update["proposal"] = _proposal(state)
        elif unavailable:
            outcome, update["error"] = "fail", unavailable[0]
        elif fatal:
            outcome, update["error"] = "fail", f"Validation failed: {detail}"
        elif chosen is None:
            # Re-planning cannot create a free room: excluding rooms only shrinks the set.
            unmet = (state.get("venue") or {}).get("unmet") or "no room was proposed"
            outcome, update["error"] = "fail", f"No room available: {unmet}"
        elif state.get("replans", 0) < MAX_REPLANS:
            outcome = "replan"
            update |= {
                "replan_needed": True,
                "replans": state.get("replans", 0) + 1,
                "replan_reason": f"Validation failed: {detail}",
                "excluded_room_ids": state.get("excluded_room_ids", []) + [chosen["room_id"]],
            }
        else:
            outcome = "fail"
            update["error"] = f"Validation still failing after {MAX_REPLANS} re-plans: {detail}"
        update["validation_outcome"] = outcome
        step = _step(
            state,
            "validate",
            started,
            rec,
            input={"attempt": attempt, "room_id": chosen["room_id"] if chosen else None},
            output={"outcome": outcome, "failed": [r for r, _ in failed]},
            error=unavailable[0] if unavailable else None,
        )
        return update | {"steps": [step]} | _seen(rec)

    def after_validate(state: State) -> str:
        return state["validation_outcome"]

    # ---------- human gate (Lab 06 Part 5) ----------

    def human_gate(state: State) -> dict[str, Any]:
        # Nothing with side effects above interrupt(): on resume the node re-runs from the top.
        latest = [
            {"rule": v["rule"], "passed": v["passed"], "message": v["message"]}
            for v in state["validation"]
            if v["attempt"] == state["validation_attempt"]
        ]
        result = state.get("quote") or {}
        decision = interrupt(
            {
                "request_id": state["request_id"],
                "revision": state.get("revision", 1),
                "proposal": {"venue": state.get("venue"), "equipment": state.get("equipment")},
                "quote": result.get("quote"),
                "validation": latest,
                "summary": result.get("officer_summary"),
            }
        )
        choice = decision["decision"]
        update: dict[str, Any] = {"decision_": choice, "nodes": ["human_gate"]}
        if choice == "revise":
            chosen = _chosen(state)
            notes = decision.get("notes") or ""
            excluded = state.get("excluded_room_ids", [])
            update |= {
                "revision_notes": notes,
                "replan_needed": True,
                "refresh_policy": True,
                "replan_reason": f"{OFFICER_REVISION}{notes}",
                "revision": state.get("revision", 1) + 1,
                # A revision is a new proposal with a fresh budget (decision 1 in the plan).
                "delegations": 0,
                "replans": 0,
                "excluded_room_ids": excluded + ([chosen["room_id"]] if chosen else []),
                "proposal": None,
            }
        return update

    def after_gate(state: State) -> str:
        return {
            "approve": "finalize",
            "revise": "supervisor",
            "reject": "rejected",
            "cancel": "cancelled",
        }[state["decision_"]]

    # ---------- endings ----------

    def finalize(state: State) -> dict[str, Any]:
        """Re-run V02 and V08 through the tools; .NET then books in one transaction."""
        request, equipment, chosen = state["request"], state.get("equipment"), _chosen(state)
        started = time.perf_counter()
        with recording() as rec:
            room = recheck_room(tools, chosen["room_id"], request["start"], request["end"])
            stock = recheck_equipment(
                tools, equipment_codes(request, equipment), request["start"], request["end"]
            )
        features = [f["code"] for f in room["room"]["features"]] if room["room"] else []
        v02 = v02_room_free(chosen["code"], room["room"] is not None, room["free"],
                            room["error"] or "", room["availability_error"])  # fmt: skip
        v08 = v08_from_recheck(request, equipment, stock, features)
        problems = [f"{rule}: {m}" for rule, (ok, m) in (("V02", v02), ("V08", v08)) if not ok]
        now = iso(clock())
        update: dict[str, Any] = {"nodes": ["finalize"], "completed_at": now}
        if problems:
            update |= {
                "status_": "failed",
                "error": "Final re-check failed: " + "; ".join(problems),
            }
        else:
            update |= {
                "status_": "completed",
                "proposal": state["proposal"] | {"finalized_at": now},
            }
        output = {"V02": {"passed": v02[0], "message": v02[1]},
                  "V08": {"passed": v08[0], "message": v08[1]}}  # fmt: skip
        step = _step(state, "finalize", started, rec, input={"room_id": chosen["room_id"]},
                     output=output, error=update.get("error"))  # fmt: skip
        return update | {"steps": [step]}

    def mark_rejected(state: State) -> dict[str, Any]:
        return {"nodes": ["rejected"], "status_": "rejected", "completed_at": iso(clock())}

    def mark_cancelled(state: State) -> dict[str, Any]:
        # The hook for "cancelling a PendingApproval request ends its paused run".
        return {"nodes": ["cancelled"], "status_": "cancelled", "completed_at": iso(clock())}

    def safe_failure(state: State) -> dict[str, Any]:
        return {
            "nodes": ["safe_failure"],
            "status_": "failed",
            "error": state.get("error") or "The run failed without a recorded reason",
            "completed_at": iso(clock()),
        }

    g = StateGraph(State)
    g.add_node("supervisor", supervisor)
    g.add_node("venue_matching", make_worker_node("venue_matching", "venue"))
    g.add_node("equipment_allocation", make_worker_node("equipment_allocation", "equipment"))
    g.add_node("policy_cost", make_worker_node("policy_cost", "quote"))
    g.add_node("validate", validate)
    g.add_node("human_gate", human_gate)
    g.add_node("finalize", finalize)
    g.add_node("rejected", mark_rejected)
    g.add_node("cancelled", mark_cancelled)
    g.add_node("safe_failure", safe_failure)
    g.add_edge(START, "supervisor")
    g.add_conditional_edges(
        "supervisor",
        route,
        {
            "venue_matching": "venue_matching",
            "equipment_allocation": "equipment_allocation",
            "policy_cost": "policy_cost",
            "validate": "validate",
            "safe_failure": "safe_failure",
        },
    )
    for worker in ("venue_matching", "equipment_allocation", "policy_cost"):
        g.add_edge(worker, "supervisor")  # every report returns to the hub
    g.add_conditional_edges(
        "validate",
        after_validate,
        {"pass": "human_gate", "replan": "supervisor", "fail": "safe_failure"},
    )
    g.add_conditional_edges(
        "human_gate",
        after_gate,
        {
            "finalize": "finalize",
            "supervisor": "supervisor",
            "rejected": "rejected",
            "cancelled": "cancelled",
        },
    )
    for node in ("finalize", "rejected", "cancelled", "safe_failure"):
        g.add_edge(node, END)
    return g.compile(checkpointer=checkpointer)


def _proposal(state: Mapping[str, Any]) -> dict[str, Any]:
    chosen = _chosen(state)
    result = state.get("quote") or {}
    return {
        "revision": state.get("revision", 1),
        "room_id": chosen["room_id"],
        "room_code": chosen["code"],
        "room_name": chosen["name"],
        "venue": state.get("venue"),
        "equipment": state.get("equipment"),
        "quote": result.get("quote"),
        "policy_flags": result.get("policy_flags", []),
        "officer_summary": result.get("officer_summary"),
    }


def _report(name: str, output: dict[str, Any] | None, error: str | None) -> str:
    if error:
        return f"[{name}] failed: {error}"
    if name == "venue_matching":
        codes = [o["code"] for o in output["options"]]
        return f"[{name}] options: {', '.join(codes) or 'none'}"
    if name == "equipment_allocation":
        lines = [f"{ln['type_code']}×{ln['qty']} ({ln['source']})" for ln in output["lines"]]
        return f"[{name}] lines: {', '.join(lines) or 'none'}"
    total = (output.get("quote") or {}).get("total")
    return f"[{name}] total: {total}"
