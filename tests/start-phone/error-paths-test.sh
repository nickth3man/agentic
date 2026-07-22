#!/usr/bin/env bash
# Error-paths test for start-phone.sh: exercise each fail() exit reason and verify
# the corresponding meta.json is written with the correct exit_reason value.
# Priority 5.
#
# Usage: bash tests/start-phone/error-paths-test.sh
#
# Covers the deterministic fail paths:
#   - api_key_missing  (env + Windows registry both empty)
#   - port_occupied    (a dummy listener holds 5123)
#
# Skipped paths (documented as manual-only — too expensive or environment-dependent
# to test deterministically without heavy mocking):
#   - prereq_missing     (would require manipulating PATH to hide dotnet/cloudflared)
#   - app_startup_failed (would require a project that fails to build)
#   - app_timeout        (would require blocking the app from responding for 120s)
#   - tunnel_startup_failed / tunnel_timeout (would require breaking cloudflared)
#
# Idempotent: kills any dummy listener it started; each test invocation creates a
# fresh logs/start-phone/<run>/ dir.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
# shellcheck source=lib/assertions.sh
source "$SCRIPT_DIR/lib/assertions.sh"

cd "$PROJECT_ROOT"

FAKE_KEY="test-only-fake-key-not-real-no-network"

# Cross-platform dummy TCP listener on 5123. Discovery order matters:
#   1. powershell — always available on Windows (this script's primary target);
#      no extra install needed. Most reliable for windows-latest CI.
#   2. python3    — Ubuntu CI default, most macOS/Linux dev machines.
#   3. python     — Windows Python convention (python3 may not exist on Windows
#                   even when Python is installed).
#   4. nc         — last resort; rare on Git Bash out of the box.
# Without powershell in the chain, windows-latest CI would silently skip the
# port_occupied test (neither python3 nor nc is guaranteed there).
start_dummy_listener() {
  if command -v powershell >/dev/null 2>&1; then
    powershell -NoProfile -Command '$l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 5123); $l.Start(); Start-Sleep -Seconds 120; $l.Stop()' >/dev/null 2>&1 &
    DUMMY_PID=$!
    return 0
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c "import socket, time
s = socket.socket()
s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.bind(('127.0.0.1', 5123))
s.listen(1)
time.sleep(120)" >/dev/null 2>&1 &
    DUMMY_PID=$!
    return 0
  elif command -v python >/dev/null 2>&1; then
    python -c "import socket, time
s = socket.socket()
s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.bind(('127.0.0.1', 5123))
s.listen(1)
time.sleep(120)" >/dev/null 2>&1 &
    DUMMY_PID=$!
    return 0
  elif command -v nc >/dev/null 2>&1; then
    nc -l 5123 >/dev/null 2>&1 &
    DUMMY_PID=$!
    return 0
  else
    return 1
  fi
}

stop_dummy_listener() {
  if [[ -n "${DUMMY_PID:-}" ]]; then
    kill "$DUMMY_PID" 2>/dev/null || true
    wait "$DUMMY_PID" 2>/dev/null || true
    DUMMY_PID=""
  fi
}

cleanup() {
  stop_dummy_listener
}
trap cleanup EXIT

# Wait for the most recent run's meta.json to appear (the script writes it in the
# cleanup trap on exit). Returns the run dir path on stdout, or empty on timeout.
wait_for_meta() {
  local deadline=$((SECONDS + 15))
  while (( SECONDS < deadline )); do
    if [[ -f logs/start-phone/LATEST ]]; then
      local latest; latest=$(cat logs/start-phone/LATEST)
      local meta="logs/start-phone/$latest/meta.json"
      if [[ -f "$meta" ]]; then
        echo "logs/start-phone/$latest"
        return 0
      fi
    fi
    sleep 1
  done
  return 1
}

# meta_field <run_dir> <field> → echoes value via jq (or empty if jq missing)
meta_field() {
  local run_dir="$1" field="$2"
  local meta="$run_dir/meta.json"
  [[ -f "$meta" ]] || return 1
  if command -v jq >/dev/null 2>&1; then
    jq -r --arg f "$field" '.[$f] // empty' "$meta"
  else
    # Crude grep fallback: matches "<field>": "value"
    grep -oE "\"$field\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" "$meta" | sed -E 's/.*: *"([^"]*)".*/\1/' | head -1
  fi
}

printf '=== Error-paths test: each fail() exit reason ===\n\n'

# --------------------------------------------------------------------------
# Test 1: api_key_missing
# --------------------------------------------------------------------------
printf -- '--- Test: api_key_missing ---\n'

# The script will try to load the key from the Windows registry via PowerShell if
# the env var is unset. For a deterministic test we need BOTH to be empty:
# - CI: env is clean, no Windows registry → deterministic.
# - Dev: env may be unset, but the registry likely has the real key → test would
#   wrongly pass because the script successfully loads the real key. Detect this
#   and skip rather than emit a false positive.
unset OPENROUTER_API_KEY
SKIP_REASON=""
if env | grep -q '^OPENROUTER_API_KEY='; then
  SKIP_REASON="OPENROUTER_API_KEY is in env (cannot deterministically test missing-key path)"
elif command -v powershell >/dev/null 2>&1 && \
     powershell -NoProfile -Command "[Environment]::GetEnvironmentVariable('OPENROUTER_API_KEY','User')" 2>/dev/null | grep -q .; then
  SKIP_REASON="OPENROUTER_API_KEY is loadable from Windows User registry (would not actually be 'missing' for the script)"
fi

if [[ -n "$SKIP_REASON" ]]; then
  skip "$SKIP_REASON"
else
  # Capture exit code WITHOUT `|| true` — that pattern would force $? to 0 and
  # mask the very failure we're testing for.
  OUTPUT=$(bash start-phone.sh 2>&1)
  EXIT_CODE=$?

  if [[ $EXIT_CODE -ne 0 ]]; then
    printf '  \u2713 Script exited non-zero when API key missing (rc=%s)\n' "$EXIT_CODE"
    START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
  else
    printf '  \u2717 Script exited 0 when API key was missing (should have failed)\n'
    START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  fi

  assert_contains "$OUTPUT" "OPENROUTER_API_KEY is not set" "Error message names the missing env var"

  # meta.json should be written with the right exit_reason
  RUN_DIR=$(wait_for_meta || true)
  if [[ -n "$RUN_DIR" ]]; then
    printf '  \u2713 meta.json written despite early fail (cleanup trap fired)\n'
    START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
    REASON=$(meta_field "$RUN_DIR" "exit_reason")
    assert_eq "api_key_missing" "$REASON" "meta.json .exit_reason is 'api_key_missing'"
    # meta.json .app_pid should be null (script never reached app startup).
    # NOTE: do NOT use jq's `// "MISSING"` fallback here — jq treats JSON null as
    # falsey and would return the fallback. Use plain `.app_pid`; jq -r outputs
    # the literal string "null" for JSON null, which is what we want to compare.
    APP_PID_VAL=$(jq -r '.app_pid' "$RUN_DIR/meta.json" 2>/dev/null || echo "JQ_MISSING")
    if [[ "$APP_PID_VAL" == "null" ]]; then
      printf '  \u2713 meta.json .app_pid is null (script never reached app startup)\n'
      START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
    else
      printf '  \u2717 meta.json .app_pid is %s (expected null)\n' "$APP_PID_VAL"
      START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
    fi
  else
    printf '  \u2717 meta.json was not written on api_key_missing fail\n'
    START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  fi
fi

# --------------------------------------------------------------------------
# Test 2: port_occupied
# --------------------------------------------------------------------------
printf '\n--- Test: port_occupied ---\n'

# Pre-check: port must be free at test start (avoid false positives)
if netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E '[:.]5123[[:space:]]' >/dev/null; then
  skip "port 5123 already occupied at test start (free it and re-run)"
else
  if ! start_dummy_listener; then
    skip "neither python3 nor nc available to occupy port 5123"
  else
    sleep 2  # let the dummy bind
    if ! netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E '[:.]5123[[:space:]]' >/dev/null; then
      skip "dummy listener failed to bind 5123"
      stop_dummy_listener
    else
      printf '  \u2713 Dummy listener occupying 5123 (PID %s)\n' "$DUMMY_PID"
      START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))

      # Set a valid API key so we get past that check and reach the port check
      export OPENROUTER_API_KEY="$FAKE_KEY"

      # Capture exit code WITHOUT `|| true` (would mask the failure we're testing).
      OUTPUT=$(bash start-phone.sh 2>&1)
      EXIT_CODE=$?

      if [[ $EXIT_CODE -ne 0 ]]; then
        printf '  \u2713 Script exited non-zero when port occupied (rc=%s)\n' "$EXIT_CODE"
        START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
      else
        printf '  \u2717 Script exited 0 when port was occupied (should have refused)\n'
        START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
      fi

      assert_contains "$OUTPUT" "Port 5123 is already occupied" "Error message names port conflict"
      assert_contains "$OUTPUT" "taskkill"                       "Error suggests taskkill remediation"

      # meta.json verification
      RUN_DIR=$(wait_for_meta || true)
      if [[ -n "$RUN_DIR" ]]; then
        REASON=$(meta_field "$RUN_DIR" "exit_reason")
        assert_eq "port_occupied" "$REASON" "meta.json .exit_reason is 'port_occupied'"
      else
        printf '  \u2717 meta.json was not written on port_occupied fail\n'
        START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
      fi

      stop_dummy_listener
      sleep 1
    fi
  fi
fi

# --------------------------------------------------------------------------
# Document the skipped paths (informational, no assertions)
# --------------------------------------------------------------------------
printf '\n--- Documented as manual-only (not asserted here) ---\n'
for reason in prereq_missing app_startup_failed app_timeout tunnel_startup_failed tunnel_timeout; do
  printf '  \u26a0 %s: requires environment mocking (PATH manipulation / project corruption / network breakage)\n' "$reason"
done

print_summary "error-paths-test"
