#!/usr/bin/env bash
# Smoke test for start-dev.sh: start, verify logs/meta, SIGTERM, verify meta.json.
# Priorities 1 (syntax), 2 (smoke), 3 (meta.json schema).
#
# Usage: bash tests/dev/smoke-test.sh
# Env:   OPENROUTER_API_KEY must be unset/empty OR set to a fake value (auto-set here).
#
# Idempotent: leaves logs/dev/<run>/ behind (one new run dir per invocation),
# kills the script it started, frees port 5123.

set -uo pipefail
# NOTE: no `set -e` — we want to run all assertions even when some fail.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
# shellcheck source=lib/assertions.sh
source "$SCRIPT_DIR/lib/assertions.sh"

cd "$PROJECT_ROOT"

# Same fake-key pattern as Agentic.Chat.Tests/ProgramTests.cs.
# Not a real key; the smoke test only loads /chat (no OpenRouter call).
export OPENROUTER_API_KEY="test-only-fake-key-not-real-no-network"

# Find a free port to override the default 5123, so this test can run in parallel
# with a developer's real start-dev.sh session. The script hardcodes 5123, so we
# cannot actually override it — instead, we require 5123 to be free at test start.
if netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E '[:.]5123[[:space:]]' >/dev/null; then
  printf '\nFATAL: port 5123 is already in use. Free it before running the smoke test.\n' >&2
  exit 1
fi

# Snapshot the existing LATEST so the polling loop can distinguish a NEW run from
# a stale previous one. Without this, the loop would match the prior run's
# script.log and proceed to assertions before the new script has actually started.
OLD_LATEST=""
[[ -f logs/dev/LATEST ]] && OLD_LATEST=$(cat logs/dev/LATEST)

cleanup_smoke() {
  if [[ -n "${SCRIPT_PID:-}" ]] && kill -0 "$SCRIPT_PID" 2>/dev/null; then
    kill -TERM "$SCRIPT_PID" 2>/dev/null || true
    # Wait up to 15s for cleanup
    for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do
      kill -0 "$SCRIPT_PID" 2>/dev/null || break
      sleep 1
    done
    kill -KILL "$SCRIPT_PID" 2>/dev/null || true
  fi
}
trap cleanup_smoke EXIT
printf '=== Smoke test: start-dev.sh full lifecycle ===\n\n'

# --------------------------------------------------------------------------
# Priority 1: bash -n syntax check (cheap, catches typos on every PR).
# --------------------------------------------------------------------------
printf -- '--- Priority 1: bash -n syntax check ---\n'
if bash -n start-dev.sh 2>smoke-syntax.err; then
  printf '  ✓ bash -n start-dev.sh passes\n'
  DEV_TEST_PASS=$((DEV_TEST_PASS+1))
else
  printf '  ✗ bash -n start-dev.sh fails:\n' >&2
  cat smoke-syntax.err >&2
  rm -f smoke-syntax.err
  DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  printf '\nAborting: cannot run a syntactically-invalid script.\n' >&2
  exit 1
fi
rm -f smoke-syntax.err

# --------------------------------------------------------------------------
# Priority 2: Start the script, verify it comes up + writes the expected logs.
# --------------------------------------------------------------------------
printf '\n--- Priority 2: start-dev.sh smoke test ---\n'
printf 'Starting start-dev.sh in background...\n'
bash start-dev.sh >/dev/null 2>&1 &
SCRIPT_PID=$!
printf 'PID: %s\n' "$SCRIPT_PID"

# Poll up to 90s for the script to bring up the app on :5123.
# CRITICAL: only accept LATEST values that differ from OLD_LATEST — otherwise
# the polling matches the prior run's script.log and proceeds prematurely.
found=""
for i in $(seq 1 18); do
  CURRENT_LATEST=""
  [[ -f logs/dev/LATEST ]] && CURRENT_LATEST=$(cat logs/dev/LATEST)
  if [[ -n "$CURRENT_LATEST" && "$CURRENT_LATEST" != "$OLD_LATEST" ]]; then
    LATEST="$CURRENT_LATEST"
    SCRIPT_LOG="logs/dev/$LATEST/script.log"
    if [[ -f "$SCRIPT_LOG" ]] && grep -q "App is responding on" "$SCRIPT_LOG" 2>/dev/null; then
      found="ready"
      break
    fi
    if grep -q "ERROR" "$SCRIPT_LOG" 2>/dev/null; then
      found="error"
      break
    fi
  fi
  if ! kill -0 "$SCRIPT_PID" 2>/dev/null; then
    found="died"
    break
  fi
  sleep 5
done

if [[ "$found" == "ready" ]]; then
  printf '  ✓ Script app responding within %ds\n' $((i*5))
  DEV_TEST_PASS=$((DEV_TEST_PASS+1))
elif [[ "$found" == "error" ]]; then
  printf '  ✗ Script errored during startup; script.log:\n'
  tail -20 "$SCRIPT_LOG" >&2
  DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  print_summary "smoke-test"
  exit 1
elif [[ "$found" == "died" ]]; then
  printf '  ✗ Script died during startup\n'
  DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  print_summary "smoke-test"
  exit 1
else
  printf '  ✗ Script did not bring app up within 90s\n'
  DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  print_summary "smoke-test"
  exit 1
fi

LATEST=$(cat logs/dev/LATEST)
LOG_DIR="logs/dev/$LATEST"
printf 'Run dir: %s\n' "$LOG_DIR"

# Verify local app responds. curl always writes the http_code via -w (000 on
# connection failure); the `|| true` just suppresses the non-zero exit.
HTTP_CODE=$(curl -sL -o /dev/null -w '%{http_code}' --max-time 5 http://localhost:5123/chat 2>/dev/null || true)
assert_eq "200" "$HTTP_CODE" "Local app responds HTTP 200 on /chat"

# Verify the expected log files exist
assert_file_exists "$LOG_DIR/script.log" "script.log exists"
assert_file_exists "$LOG_DIR/app.log"    "app.log exists"

# Verify script.log has the expected narrative content
if [[ -f "$LOG_DIR/script.log" ]]; then
  SCRIPT_LOG_CONTENT=$(cat "$LOG_DIR/script.log")
  assert_contains "$SCRIPT_LOG_CONTENT" "Run ID:"     "script.log logs Run ID"
  assert_contains "$SCRIPT_LOG_CONTENT" "Started at:" "script.log logs start time"
  assert_contains "$SCRIPT_LOG_CONTENT" "App is responding on" "script.log confirms app is responding"
fi

# Verify app.log has hot-reload signature (dotnet watch verbose markers)
if [[ -f "$LOG_DIR/app.log" ]]; then
  APP_LOG_CONTENT=$(cat "$LOG_DIR/app.log")
  assert_contains "$APP_LOG_CONTENT" "dotnet watch"      "app.log shows dotnet watch"
  assert_contains "$APP_LOG_CONTENT" "Now listening on:" "app.log shows Kestrel startup"
fi

# meta.json should NOT exist while the script is still running (postmortem-only)
if [[ ! -f "$LOG_DIR/meta.json" ]]; then
  printf '  ✓ meta.json correctly absent during run (postmortem-only by design)\n'
  DEV_TEST_PASS=$((DEV_TEST_PASS+1))
else
  printf '  ✗ meta.json exists during run (should be written only on exit)\n'
  DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
fi

# --------------------------------------------------------------------------
# SIGTERM the script and verify the cleanup + meta.json contract.
# --------------------------------------------------------------------------
printf '\n--- Sending SIGTERM, verifying cleanup contract ---\n'
kill -TERM "$SCRIPT_PID" 2>/dev/null || true

# Poll for cleanup COMPLETION, not process exit. `kill -0` returns success for
# zombie PIDs (POSIX) — a process that has exited but not yet been reaped still
# answers `kill -0`. On Git Bash / MSYS2 the PID layer adds latency on top of
# that, making `kill -0` doubly unreliable for detecting exit. Instead, poll
# for the script's "I finished cleanup" contract: meta.json exists (proves the
# trap ran and write_meta fired) AND port 5123 is free (proves the children
# are dead, not just the script exiting).
poll_start=$SECONDS
cleanup_done=0
deadline=$((poll_start + 20))
while (( SECONDS < deadline )); do
  if [[ -f "$LOG_DIR/meta.json" ]]; then
    # meta.json exists — cleanup ran. Give children a moment to fully release
    # the port, then verify port is free.
    sleep 2
    if ! netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E "[:.]5123[[:space:]]" >/dev/null; then
      elapsed=$((SECONDS - poll_start))
      printf '  ✓ Script cleanup completed within %ds of SIGTERM (meta.json written + port freed)\n' "$elapsed"
      DEV_TEST_PASS=$((DEV_TEST_PASS+1))
      cleanup_done=1
      break
    fi
  fi
  sleep 1
done
if [[ $cleanup_done -eq 0 ]]; then
  printf '  ✗ Script did not complete cleanup within 20s of SIGTERM (forcing kill)\n'
  DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  kill -KILL "$SCRIPT_PID" 2>/dev/null || true
fi

# --------------------------------------------------------------------------
# Priority 3: meta.json schema + structured postmortem
# --------------------------------------------------------------------------
printf '\n--- Priority 3: meta.json schema ---\n'
assert_file_exists "$LOG_DIR/meta.json" "meta.json exists after SIGTERM"

if [[ -f "$LOG_DIR/meta.json" ]]; then
  META=$(cat "$LOG_DIR/meta.json")

  # Validate JSON well-formedness (jq presence required for full schema check)
  if command -v jq >/dev/null 2>&1; then
    if echo "$META" | jq -e . >/dev/null 2>&1; then
      printf '  ✓ meta.json is valid JSON\n'
      DEV_TEST_PASS=$((DEV_TEST_PASS+1))
    else
      printf '  ✗ meta.json is not valid JSON\n'
      DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
    fi

    # Required fields present and non-null (a regression here breaks any agent
    # or tool that consumes meta.json as a contract).
    for field in run_id started_at ended_at duration_seconds app_pid local_url log_dir; do
      val=$(echo "$META" | jq -r ".$field" 2>/dev/null || echo "JQ_ERROR")
      if [[ "$val" != "null" && "$val" != "JQ_ERROR" && -n "$val" ]]; then
        printf '  ✓ meta.json has non-null .%s\n' "$field"
        DEV_TEST_PASS=$((DEV_TEST_PASS+1))
      else
        printf '  ✗ meta.json missing or null .%s\n' "$field"
        DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
      fi
    done

    # Tunnel fields must be absent (post-tunnel removal contract).
    for field in tunnel_pid public_url; do
      has=$(echo "$META" | jq "has(\"$field\")" 2>/dev/null || echo "JQ_ERROR")
      if [[ "$has" == "false" ]]; then
        printf '  ✓ meta.json does not have .%s (tunnel removed)\n' "$field"
        DEV_TEST_PASS=$((DEV_TEST_PASS+1))
      else
        printf '  ✗ meta.json unexpectedly has .%s\n' "$field"
        DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
      fi
    done

    # Specific value checks
    EXIT_REASON_VAL=$(echo "$META" | jq -r '.exit_reason')
    assert_eq "terminated" "$EXIT_REASON_VAL" "meta.json .exit_reason is 'terminated' (SIGTERM)"

    LOCAL_URL_VAL=$(echo "$META" | jq -r '.local_url')
    assert_eq "http://localhost:5123" "$LOCAL_URL_VAL" "meta.json .local_url is the expected value"

    DURATION_VAL=$(echo "$META" | jq -r '.duration_seconds')
    if [[ "$DURATION_VAL" =~ ^[0-9]+$ ]] && (( DURATION_VAL > 0 )); then
      printf '  ✓ meta.json .duration_seconds is a positive integer (%s)\n' "$DURATION_VAL"
      DEV_TEST_PASS=$((DEV_TEST_PASS+1))
    else
      printf '  ✗ meta.json .duration_seconds is not a positive integer: %s\n' "$DURATION_VAL"
      DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
    fi

    # Schema shape: .files nested object with script/app only
    FILES_VALID=$(echo "$META" | jq -e '.files.script == "script.log" and .files.app == "app.log" and (.files | has("tunnel") | not)' >/dev/null 2>&1 && echo yes || echo no)
    assert_eq "yes" "$FILES_VALID" "meta.json .files has {script,app} shape (no files.tunnel)"
  else
    skip "jq not installed — using grep fallback for schema checks"
    assert_contains "$META" '"exit_reason": "terminated"'           "meta.json exit_reason is terminated (grep fallback)"
    assert_contains "$META" '"local_url": "http://localhost:5123"'  "meta.json local_url is correct (grep fallback)"
    assert_not_contains "$META" '"tunnel_pid"' "meta.json has no tunnel_pid field (grep fallback)"
    assert_not_contains "$META" '"public_url"' "meta.json has no public_url field (grep fallback)"
  fi
fi

# Verify port freed
sleep 2
if netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E '[:.]5123[[:space:]]' >/dev/null; then
  printf '  ✗ Port 5123 still occupied after shutdown\n'
  DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
else
  printf '  ✓ Port 5123 freed after shutdown\n'
  DEV_TEST_PASS=$((DEV_TEST_PASS+1))
fi

print_summary "smoke-test"
