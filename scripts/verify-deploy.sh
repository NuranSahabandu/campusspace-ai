#!/usr/bin/env bash
# Checks the deployed system end to end (Task 6.D3, docs/deploy/RUNBOOK.md step 6). Needs curl and jq; holds no secret.
#
#   ./scripts/verify-deploy.sh --api https://campusspace-api.onrender.com \
#       --agent https://campusspace-agent.onrender.com --web https://<app>.vercel.app [--email perera@campusspace.local]
#
# or set API_URL, AGENT_URL, WEB_URL (and VERIFY_EMAIL). The demo password is asked with a hidden prompt; it is sent to
# the API's login only, through stdin (never on a command line), and never stored or printed. So is the token.
# --local adds X-Forwarded-Proto: https to API calls, as Render's proxy does (for a rehearsal against local containers).
# Exit code = the number of failed checks. Free Render services sleep: the first calls may take a minute or two.
set -uo pipefail

api="${API_URL:-}"
agent="${AGENT_URL:-}"
web="${WEB_URL:-}"
email="${VERIFY_EMAIL:-perera@campusspace.local}"
local_proxy=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --api) api="$2"; shift 2 ;;
    --agent) agent="$2"; shift 2 ;;
    --web) web="$2"; shift 2 ;;
    --email) email="$2"; shift 2 ;;
    --local) local_proxy=true; shift ;;
    -h|--help) sed -n '2,11p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $1 (see --help)" >&2; exit 2 ;;
  esac
done
for tool in curl jq; do
  command -v "$tool" >/dev/null || { echo "error: $tool is required" >&2; exit 2; }
done
if [[ -z "$api" || -z "$agent" ]]; then
  echo "error: give --api and --agent (and --web), or set API_URL / AGENT_URL / WEB_URL" >&2
  exit 2
fi
api="${api%/}"; agent="${agent%/}"; web="${web%/}"

failures=0
pass() { printf '  PASS  %s\n' "$1"; }
fail() { printf '  FAIL  %s\n' "$1"; failures=$((failures + 1)); }
skip() { printf '  SKIP  %s\n' "$1"; }
check() { if [[ "$2" == true ]]; then pass "$1"; else fail "$1${3:+ ($3)}"; fi; }

proxy_header=()
$local_proxy && proxy_header=(-H "X-Forwarded-Proto: https")

body="$(mktemp "${TMPDIR:-/tmp}/verify-deploy.XXXXXX")"
headers="$(mktemp "${TMPDIR:-/tmp}/verify-deploy.XXXXXX")"
trap 'rm -f "$body" "$headers"' EXIT

# GET (or another method via extra curl args): the status code on stdout, the body in $body, the headers in $headers.
# --max-time 120: a sleeping free Render service takes about a minute to wake.
http() {
  local url="$1"; shift
  curl -sS -o "$body" -D "$headers" -w '%{http_code}' --max-time 120 "$@" "$url" 2>/dev/null || true
}
api_http() { local path="$1"; shift; http "$api$path" ${proxy_header[@]+"${proxy_header[@]}"} "$@"; }
json() { jq -r "$1" "$body" 2>/dev/null; }
header() { grep -i "^$1:" "$headers" | head -1 | cut -d' ' -f2- | tr -d '\r'; }

echo "Liveness (Render's health check path; wakes a sleeping service)"
check "agent /health/live 200" "$([[ $(http "$agent/health/live") == 200 ]] && echo true)"
check "API /health/live 200" "$([[ $(api_http /health/live) == 200 ]] && echo true)"

echo "Health with dependencies (the evidence URLs)"
code="$(http "$agent/health")"
check "agent /health 200" "$([[ $code == 200 ]] && echo true)" "HTTP $code"
check "agent checkpointer is postgres and reachable" \
  "$([[ $(json '.checkpointer') == postgres && $(json '.checkpointer_ok') == true ]] && echo true)" \
  "checkpointer=$(json '.checkpointer') ok=$(json '.checkpointer_ok')"
echo "        agents: $(json '.agents | to_entries | map("\(.key)=\(.value)") | join(" ")')"
echo "        gemini key configured: $(json '.google_api_key_configured'); fault injection: $(json '.fault_injection')"
check "agent fault injection off" "$([[ $(json '.fault_injection') == null ]] && echo true)"

code="$(api_http /health)"
check "API /health 200" "$([[ $code == 200 ]] && echo true)" "HTTP $code"
check "API database Healthy" "$([[ $(json '.checks[] | select(.name=="database") | .status') == Healthy ]] && echo true)"
check "API sees the agent service (agent-service Healthy)" \
  "$([[ $(json '.checks[] | select(.name=="agent-service") | .status') == Healthy ]] && echo true)" \
  "$(json '.checks[] | select(.name=="agent-service") | .status'); check AgentService__BaseUrl"

echo "Swagger"
code="$(api_http /swagger/v1/swagger.json)"
check "swagger.json 200" "$([[ $code == 200 ]] && echo true)" "HTTP $code"
check "swagger lists /api routes and no /internal route" \
  "$([[ $(json '[.paths | keys[] | select(startswith("/api"))] | length') -gt 0 \
       && $(json '[.paths | keys[] | select(ascii_downcase | startswith("/internal"))] | length') == 0 ]] && echo true)"
check "Swagger UI page 200" "$([[ $(api_http /swagger/index.html) == 200 ]] && echo true)"

echo "CORS"
preflight() {
  api_http /api/auth/me -X OPTIONS -H "Origin: $1" -H "Access-Control-Request-Method: GET" \
    -H "Access-Control-Request-Headers: authorization" >/dev/null
  header Access-Control-Allow-Origin
}
if [[ -n "$web" ]]; then
  allowed="$(preflight "$web")"
  check "preflight from $web is allowed" "$([[ "$allowed" == "$web" ]] && echo true)" "Access-Control-Allow-Origin: ${allowed:-none}"
else
  skip "preflight from the web origin (no --web)"
fi
refused="$(preflight https://evil.example)"
check "preflight from https://evil.example is refused" "$([[ -z "$refused" ]] && echo true)" "got: $refused"

echo "React (Vercel)"
if [[ -n "$web" ]]; then
  code="$(http "$web/")"
  check "web / 200 with the app shell" "$([[ $code == 200 ]] && grep -q 'id="root"' "$body" && echo true)" "HTTP $code"
  # The entry script and the chunks index.html preloads (the API client may sit in either).
  scripts="$(grep -oE '(src|href)="/assets/[^"]+\.js"' "$body" | cut -d'"' -f2)"
  code="$(http "$web/requests/123")"
  check "deep link /requests/123 serves index.html (SPA rewrite)" \
    "$([[ $code == 200 ]] && grep -q 'id="root"' "$body" && echo true)" "HTTP $code"
  found=false
  for script_path in $scripts; do
    http "$web$script_path" >/dev/null
    if grep -qF "$api" "$body"; then found=true; break; fi
  done
  check "the bundle calls this API (VITE_API_URL)" "$found" \
    "$api not in the scripts index.html loads; set VITE_API_URL in Vercel and redeploy"
else
  skip "web checks (no --web)"
fi

echo "Sign-in and an authenticated call ($email)"
if [[ ! -t 0 ]]; then
  skip "login (no terminal for the hidden password prompt)"
else
  printf '  Demo password for %s (hidden; from the report, not the README): ' "$email"
  IFS= read -rs password
  printf '\n'
  # The JSON body is built by jq from stdin and sent with --data @-: the password is never an argument of any process.
  code="$(printf '%s' "$password" | jq -Rs --arg email "$email" '{email: $email, password: .}' \
    | api_http /api/auth/login -X POST -H 'Content-Type: application/json' --data @-)"
  unset password
  token="$(json '.accessToken // empty')"
  : > "$body"
  check "POST /api/auth/login 200 with a token" "$([[ $code == 200 && -n "$token" ]] && echo true)" "HTTP $code"
  if [[ -n "$token" ]]; then
    code="$(printf 'Authorization: Bearer %s' "$token" | api_http /api/auth/me -H @-)"
    check "GET /api/auth/me 200 as $email" "$([[ $code == 200 && $(json '.email') == "$email" ]] && echo true)" "HTTP $code"
    echo "        role: $(json '.role')"
    code="$(printf 'Authorization: Bearer %s' "$token" | api_http '/api/rooms?page=1&pageSize=5' -H @-)"
    check "GET /api/rooms 200 with the seeded rooms" "$([[ $code == 200 && $(json '.total') -gt 0 ]] && echo true)" \
      "HTTP $code, total $(json '.total')"
    unset token
  fi
fi

echo
if [[ $failures -eq 0 ]]; then echo "All checks passed."; else echo "$failures check(s) failed."; fi
exit "$failures"
