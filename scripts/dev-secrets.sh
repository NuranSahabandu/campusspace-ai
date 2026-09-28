#!/usr/bin/env bash
# Copies the API's secrets from .env into dotnet user-secrets (CampusSpace.Api).
# Safe to re-run: `user-secrets set` overwrites. Never prints a secret value.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"
project="$repo_root/backend/CampusSpace.Api"

# .env names use "__" (env-var style); user-secrets keys use ":".
keys=(
  ConnectionStrings__Default
  Jwt__Key
  Jwt__Issuer
  Jwt__Audience
  AgentService__BaseUrl
  AgentService__ServiceKey
  AgentTools__Key
)

if [[ ! -f "$env_file" ]]; then
  echo "error: $env_file not found. Run: cp .env.example .env" >&2
  exit 1
fi

# Parse .env without sourcing it, so nothing in the file is executed.
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

missing=()
for key in "${keys[@]}"; do
  value="$(lookup "$key" || true)"
  if [[ -z "$value" || "$value" == *change-me* ]]; then
    missing+=("$key")
  fi
done
if (( ${#missing[@]} > 0 )); then
  echo "error: set these in .env first: ${missing[*]}" >&2
  exit 1
fi

# X-Service-Key (.NET -> agent) and X-Agent-Key (agent tools -> .NET) must be different secrets (plan §15.3).
if [[ "$(lookup AgentService__ServiceKey)" == "$(lookup AgentTools__Key)" ]]; then
  echo "error: AgentService__ServiceKey and AgentTools__Key must differ. Generate each with: openssl rand -hex 32" >&2
  exit 1
fi

for key in "${keys[@]}"; do
  value="$(lookup "$key")"
  # `set` echoes the value on stdout, so discard it.
  dotnet user-secrets set "${key//__/:}" "$value" --project "$project" >/dev/null
done

echo "set ${#keys[@]} secrets: ${keys[*]//__/:}"
