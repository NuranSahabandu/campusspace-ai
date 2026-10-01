#!/usr/bin/env bash
# Neon database set-up for the deployment (Task 6.D3, docs/deploy/RUNBOOK.md). Run from your Mac, as the Neon OWNER
# role (e.g. neondb_owner) on the DIRECT endpoint (Neon console > Connect > "Connection pooling" OFF).
#
#   ./scripts/neon-db.sh roles                    # databases, roles, grants (docs/deploy/neon-roles.sql), then check
#   ./scripts/neon-db.sh password <role>          # set campusspace_app's or campusspace_agent's password (hidden prompt)
#   ./scripts/neon-db.sh migrate                  # apply the EF migrations (idempotent script), then check
#   ./scripts/neon-db.sh check                    # read-only report (docs/deploy/neon-check.sql)
#
# Secrets: the owner connection URL is read with a hidden prompt (or from NEON_OWNER_URL, if you exported it in this
# shell yourself). It is never written to a file, never put on a command line and never printed: it is split into the
# standard libpq variables (PGHOST, PGUSER, PGPASSWORD, ...) that psql reads from its environment. psql runs from the
# postgres:16 Docker image (no local install needed). A role password typed at `password` is hashed by psql
# (SCRAM-SHA-256) before it is sent, so the server never receives it in clear text.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
business_db=campusspace
psql_image=postgres:16
roles=(campusspace_app campusspace_agent)

usage() {
  sed -n '2,8p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

[[ $# -ge 1 ]] || usage
command="$1"
shift

if ! docker info >/dev/null 2>&1; then
  echo "error: Docker is not running (psql runs from the $psql_image image). Start Docker Desktop and try again." >&2
  exit 1
fi

# Splits a postgres(ql)://user:password@host[:port]/db[?params] URL into PG* variables (exported, never printed).
load_url() {
  local url="$1"
  local re='^postgres(ql)?://([^:/@]+):([^@]+)@([^/:?]+)(:([0-9]+))?/([^?]+)(\?(.*))?$'
  if [[ ! "$url" =~ $re ]]; then
    echo "error: that is not a postgresql://user:password@host/database URL (copy it from Neon > Connect)." >&2
    exit 1
  fi
  local query="${BASH_REMATCH[9]}" pair
  export PGUSER="${BASH_REMATCH[2]}"
  # Percent-decode the password (Neon's are plain, but a hand-set one may contain encoded characters).
  local encoded="${BASH_REMATCH[3]}"
  PGPASSWORD="$(printf '%b' "${encoded//%/\\x}")"
  export PGPASSWORD
  export PGHOST="${BASH_REMATCH[4]}"
  export PGPORT="${BASH_REMATCH[6]:-5432}"
  export PGDATABASE="${BASH_REMATCH[7]}"
  export PGSSLMODE=require
  export PGCHANNELBINDING=prefer
  local pairs=()
  [[ -n "$query" ]] && IFS='&' read -r -a pairs <<< "$query"
  for pair in ${pairs[@]+"${pairs[@]}"}; do
    case "$pair" in
      sslmode=*) PGSSLMODE="${pair#sslmode=}" ;;
      channel_binding=*) PGCHANNELBINDING="${pair#channel_binding=}" ;;
    esac
  done
  export PGAPPNAME=campusspace-neon-db
  if [[ "$PGHOST" == *-pooler.* ]]; then
    echo "error: this is the POOLED endpoint (-pooler in the host). Use the DIRECT one (Neon > Connect, pooling off):" \
         "role and database DDL need a direct connection." >&2
    exit 1
  fi
  if [[ "$PGSSLMODE" == disable || "$PGSSLMODE" == allow || "$PGSSLMODE" == prefer ]] && [[ -z "${NEON_DB_ALLOW_PLAINTEXT:-}" ]]; then
    echo "error: sslmode=$PGSSLMODE; Neon needs sslmode=require (or verify-full)." >&2
    exit 1
  fi
}

read_owner_url() {
  local url="${NEON_OWNER_URL:-}"
  if [[ -z "$url" ]]; then
    printf 'Owner connection URL (DIRECT endpoint; input hidden): ' >&2
    IFS= read -rs url
    printf '\n' >&2
  fi
  [[ -n "$url" ]] || { echo "error: no URL given" >&2; exit 1; }
  load_url "$url"
  echo "connecting as $PGUSER to $PGHOST, database $PGDATABASE" >&2
}

# psql from the postgres:16 image. -e NAME (without =value) copies the variable from this script's environment, so no
# secret is on any command line. NEON_DB_DOCKER_NETWORK is only for the local test against a throwaway container.
run_psql() {
  local tty=()
  if [[ "${1:-}" == --tty ]]; then
    tty=(-t)
    shift
  fi
  docker run --rm -i ${tty[@]+"${tty[@]}"} --network "${NEON_DB_DOCKER_NETWORK:-bridge}" \
    -e PGHOST -e PGPORT -e PGUSER -e PGPASSWORD -e PGDATABASE -e PGSSLMODE -e PGCHANNELBINDING -e PGAPPNAME \
    "$psql_image" psql -X -v ON_ERROR_STOP=1 "$@"
}

check() {
  run_psql -q < "$repo_root/docs/deploy/neon-check.sql"
}

case "$command" in
  roles)
    read_owner_url
    run_psql -q < "$repo_root/docs/deploy/neon-roles.sql"
    echo "roles and databases ready. Next: ./scripts/neon-db.sh password campusspace_app (and campusspace_agent)" >&2
    check
    ;;
  password)
    role="${1:-}"
    if [[ ! " ${roles[*]} " == *" $role "* ]]; then
      echo "error: give one of: ${roles[*]}" >&2
      exit 2
    fi
    if [[ ! -t 0 ]]; then
      echo "error: run this in a terminal (psql asks for the password)" >&2
      exit 1
    fi
    read_owner_url
    printf '%s\n' "Paste the new password for $role twice (hidden). Generate it first:" \
      "  openssl rand -hex 32 | tr -d '\n' | pbcopy" >&2
    run_psql --tty -d "$business_db" -c "\\password $role"
    echo "password set for $role" >&2
    ;;
  migrate)
    if ! dotnet ef --version >/dev/null 2>&1; then
      echo "error: dotnet-ef is missing. Install it: dotnet tool install -g dotnet-ef --version \"8.*\"" >&2
      exit 1
    fi
    script="$(mktemp "${TMPDIR:-/tmp}/campusspace-migrate.XXXXXX")"
    trap 'rm -f "$script"' EXIT
    # The script is plain DDL (no secret in it). --idempotent: every migration checks __EFMigrationsHistory first, so a
    # re-run applies only what is missing. Generated before the prompt, so a build error doesn't waste your paste.
    echo "generating the idempotent migration script (dotnet ef) ..." >&2
    dotnet ef migrations script --idempotent --project "$repo_root/backend/CampusSpace.Api" -o "$script" >/dev/null
    read_owner_url
    export PGDATABASE="$business_db"
    run_psql -q < "$script"
    echo "migrations applied to $business_db" >&2
    check
    ;;
  check)
    read_owner_url
    check
    ;;
  *)
    usage
    ;;
esac
