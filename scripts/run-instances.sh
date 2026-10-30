#!/usr/bin/env bash
#
# Starts N local instances of the API on consecutive ports (BASE_PORT, BASE_PORT+1, ...).
#
# Every instance gets its own port, instance id (<ID_PREFIX>-<n>) and log file
# (zerovp-<id>-yyyyMMdd.log). All instances use ONE shared JSON storage file
# (JsonPersistence__Shared=true), so a task created through one instance is visible through
# every other. Press Ctrl+C to stop all of them.
# See the README section "Vice instanci na localhostu".
#
# Usage:   scripts/run-instances.sh [COUNT] [BASE_PORT]
# Env:     ID_PREFIX (default api), ASPNETCORE_ENVIRONMENT (default Development),
#          CONFIGURATION (Release|Debug, default Release), NO_BUILD=1 (skip the build),
#          DATA_FILE (shared JSON file, default <repo>/data/todos.json)
# Example: bash scripts/run-instances.sh 3 5258

set -euo pipefail

COUNT="${1:-3}"
BASE_PORT="${2:-5258}"
ID_PREFIX="${ID_PREFIX:-api}"
ENVIRONMENT_NAME="${ASPNETCORE_ENVIRONMENT:-Development}"
CONFIGURATION="${CONFIGURATION:-Release}"
NO_BUILD="${NO_BUILD:-0}"

is_number() { [[ "$1" =~ ^[0-9]+$ ]]; }

if ! is_number "$COUNT" || (( COUNT < 1 || COUNT > 50 )); then
  echo "COUNT must be a number between 1 and 50 (got: $COUNT)." >&2
  exit 2
fi
if ! is_number "$BASE_PORT" || (( BASE_PORT < 1 || BASE_PORT + COUNT - 1 > 65535 )); then
  echo "BASE_PORT $BASE_PORT with $COUNT instance(s) is out of the port range 1-65535." >&2
  exit 2
fi
if [[ ! "$ID_PREFIX" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "ID_PREFIX may contain only letters, digits, '.', '_' and '-'." >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_DIR="$ROOT/src/UAI.ZeroVP.API"
DLL="$PROJECT_DIR/bin/$CONFIGURATION/net10.0/UAI.ZeroVP.API.dll"
DATA_FILE="${DATA_FILE:-$ROOT/data/todos.json}"

# Fail early with a clear message instead of a crashing instance.
for ((i = 0; i < COUNT; i++)); do
  port=$((BASE_PORT + i))
  if (echo > "/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
    echo "Port $port is already in use. Choose another BASE_PORT." >&2
    exit 1
  fi
done

if [[ "$NO_BUILD" != "1" ]]; then
  # Build once. Several parallel "dotnet run" would race on the same obj/bin folders.
  echo "==> Building ($CONFIGURATION)"
  dotnet build "$PROJECT_DIR/UAI.ZeroVP.API.csproj" --configuration "$CONFIGURATION"
fi

if [[ ! -f "$DLL" ]]; then
  echo "Binaries not found: $DLL. Run without NO_BUILD=1 or use the matching CONFIGURATION." >&2
  exit 1
fi

pids=()

stop_all() {
  trap - INT TERM EXIT
  for pid in "${pids[@]:-}"; do
    [[ -n "$pid" ]] && kill "$pid" 2>/dev/null || true   # SIGTERM = graceful shutdown, flushes the log
  done
  wait 2>/dev/null || true
}
trap stop_all INT TERM EXIT

for ((i = 1; i <= COUNT; i++)); do
  port=$((BASE_PORT + i - 1))
  id="$ID_PREFIX-$i"

  # --contentRoot: appsettings.json and the logs/ folder are taken from the project folder,
  # exactly like with "dotnet run".
  ASPNETCORE_ENVIRONMENT="$ENVIRONMENT_NAME" \
  Instance__Id="$id" \
  FileLogging__IncludeInstanceId=true \
  JsonPersistence__Enabled=true \
  JsonPersistence__Shared=true \
  JsonPersistence__FilePath="$DATA_FILE" \
    dotnet "$DLL" --urls "http://localhost:$port" --contentRoot "$PROJECT_DIR" &
  pids+=("$!")

  printf '==> %-10s http://localhost:%s  (log: src/UAI.ZeroVP.API/logs/zerovp-%s-*.log)\n' "$id" "$port" "$id"
done

echo
echo "Shared storage: $DATA_FILE"
echo "Started $COUNT instance(s). Check them with:  curl http://localhost:$BASE_PORT/api/info"
echo "Press Ctrl+C to stop all instances."

wait