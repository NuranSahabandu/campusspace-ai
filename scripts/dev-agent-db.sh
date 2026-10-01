#!/usr/bin/env bash
# Creates the agent service's own checkpoint role and database in the Docker PostgreSQL (Task 6.D2), and puts
# AGENT_CHECKPOINT_URL in .env when it is empty or missing. Safe to re-run. Never prints a secret value.
#
# The role can reach ONLY its own database (campusspace_agent, schema agent_checkpoints): CONNECT on the business
# database is revoked from PUBLIC, so the agent service still has no access to any business table (plan §7.1 rule 3,
# narrowed as §10.9 anticipates: credentials for its checkpoints only).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"

agent_role=campusspace_agent
agent_db=campusspace_agent
agent_schema=agent_checkpoints
min_password_length=32

if [[ ! -f "$env_file" ]]; then
  echo "error: $env_file not found. Run: cp .env.example .env" >&2
  exit 1
fi

# Parse .env without sourcing it, so nothing in the file is executed (same parser as dev-secrets.sh).
lookup() {
  local wanted="$1" name value
  while IFS='=' read -r name value || [[ -n "$name" ]]; do
    name="${name#"${name%%[![:space:]]*}"}"
    [[ -z "$name" || "$name" == \#* ]] && continue
    [[ "$name" != "$wanted" ]] && continue
    value="${value%$'\r'}"
    value="${value#\"}"; value="${value%\"}"
    value="${value#\'}"; value="${value%\'}"
    printf '%s' "$value"
    return 0
  done < "$env_file"
  return 1
}

pg_user="$(lookup POSTGRES_USER || true)"
pg_db="$(lookup POSTGRES_DB || true)"
pg_port="$(lookup POSTGRES_PORT || true)"
pg_port="${pg_port:-5432}"
agent_password="$(lookup AGENT_DB_PASSWORD || true)"

# Identifiers are spliced into SQL, so allow plain lower-case names only.
for pair in "POSTGRES_USER=$pg_user" "POSTGRES_DB=$pg_db"; do
  if [[ ! "${pair#*=}" =~ ^[a-z_][a-z0-9_]*$ ]]; then
    echo "error: ${pair%%=*} in .env must be a plain lower-case name (letters, digits, _)" >&2
    exit 1
  fi
done
if [[ ! "$pg_port" =~ ^[0-9]+$ ]]; then
  echo "error: POSTGRES_PORT in .env must be a number" >&2
  exit 1
fi
# Hex only, so the value is safe inside a SQL literal and a postgresql:// URL without escaping.
if [[ -z "$agent_password" || "$agent_password" == *change-me* ]]; then
  echo "error: set AGENT_DB_PASSWORD in .env first (generate: openssl rand -hex 16, or -hex 32)" >&2
  exit 1
fi
if [[ ! "$agent_password" =~ ^[0-9a-fA-F]+$ || ${#agent_password} -lt $min_password_length ]]; then
  echo "error: AGENT_DB_PASSWORD must be hex only and at least $min_password_length characters" \
       "(openssl rand -hex 16, or -hex 32)" >&2
  exit 1
fi
if [[ "$agent_password" == "$(lookup POSTGRES_PASSWORD || true)" ]]; then
  echo "error: AGENT_DB_PASSWORD must differ from POSTGRES_PASSWORD" >&2
  exit 1
fi

cd "$repo_root"
if [[ -z "$(docker compose ps --status running --quiet db 2>/dev/null)" ]]; then
  echo "error: the db container is not running. Run: docker compose up -d" >&2
  exit 1
fi

# psql as the superuser over the container's local socket. The SQL (and the password inside it) goes through stdin,
# never a command line. Output is captured and the password redacted before anything is shown.
run_psql() {
  local database="$1" sql="$2" out
  if ! out="$(printf '%s\n' "$sql" | docker compose exec -T db \
      psql -X -q -v ON_ERROR_STOP=1 -U "$pg_user" -d "$database" 2>&1)"; then
    echo "error: psql failed in database $database:" >&2
    printf '%s\n' "${out//"$agent_password"/<redacted>}" >&2
    exit 1
  fi
}

run_psql "$pg_db" "
\\set VERBOSITY terse
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$agent_role') THEN
    CREATE ROLE $agent_role LOGIN PASSWORD '$agent_password'
      NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
  ELSE
    ALTER ROLE $agent_role LOGIN PASSWORD '$agent_password'
      NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
  END IF;
END
\$\$;
SELECT 'CREATE DATABASE $agent_db OWNER $agent_role'
  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$agent_db') \\gexec
-- Only the agent role (and the superuser) may connect to the checkpoint database, and the agent role may not
-- connect to the business database: CONNECT comes from PUBLIC by default, so revoke it there.
REVOKE CONNECT ON DATABASE $agent_db FROM PUBLIC;
GRANT CONNECT ON DATABASE $agent_db TO $agent_role;
REVOKE CONNECT ON DATABASE $pg_db FROM PUBLIC;
GRANT CONNECT ON DATABASE $pg_db TO $pg_user;
ALTER ROLE $agent_role IN DATABASE $agent_db SET search_path = $agent_schema;
"

run_psql "$agent_db" "
\\set VERBOSITY terse
CREATE SCHEMA IF NOT EXISTS $agent_schema AUTHORIZATION $agent_role;
"

# Self-check (read-only), over the local socket: the agent role can connect to its own database and not to the
# business one.
if ! docker compose exec -T db psql -X -q -U "$agent_role" -d "$agent_db" -tAc "select 1" >/dev/null 2>&1; then
  echo "error: self-check failed: $agent_role cannot connect to $agent_db" >&2
  exit 1
fi
if docker compose exec -T db psql -X -q -U "$agent_role" -d "$pg_db" -tAc "select 1" >/dev/null 2>&1; then
  echo "error: self-check failed: $agent_role can connect to the business database $pg_db" >&2
  exit 1
fi
echo "role $agent_role and database $agent_db (schema $agent_schema) ready; $agent_role cannot connect to $pg_db"

# AGENT_CHECKPOINT_URL: written only when empty or missing, so a URL you set yourself is never replaced.
url="postgresql://$agent_role:$agent_password@localhost:$pg_port/$agent_db"
current="$(lookup AGENT_CHECKPOINT_URL || true)"
if [[ -n "$current" ]]; then
  if [[ "$current" == "$url" ]]; then
    echo "AGENT_CHECKPOINT_URL in .env: unchanged (already points at $agent_db)"
  else
    echo "AGENT_CHECKPOINT_URL in .env: unchanged (it has another value; left alone)"
  fi
  exit 0
fi

# Rewrite .env in pure bash (the value never reaches another process's arguments), keeping the file's inode and
# permissions, and every other line as it is.
tmp="$(mktemp "${TMPDIR:-/tmp}/dev-agent-db.XXXXXX")"
trap 'rm -f "$tmp"' EXIT
replaced=false
while IFS= read -r line || [[ -n "$line" ]]; do
  if [[ "$replaced" == false && "$line" =~ ^[[:space:]]*AGENT_CHECKPOINT_URL= ]]; then
    printf 'AGENT_CHECKPOINT_URL=%s\n' "$url"
    replaced=true
  else
    printf '%s\n' "$line"
  fi
done < "$env_file" > "$tmp"
if [[ "$replaced" == false ]]; then
  printf 'AGENT_CHECKPOINT_URL=%s\n' "$url" >> "$tmp"
fi
cat "$tmp" > "$env_file"
echo "AGENT_CHECKPOINT_URL in .env: set (points at $agent_db on localhost:$pg_port)"
