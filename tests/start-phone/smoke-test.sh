#!/usr/bin/env bash
# Smoke test for start-phone.sh: start, verify logs/meta, SIGTERM, verify meta.json.
# Priorities 1 (syntax), 2 (smoke), 3 (meta.json schema).
#
# Usage: bash tests/start-phone/smoke-test.sh
# Env:   OPENROUTER_API_KEY must be unset/empty OR set to a fake value (auto-set here).
#
# Idempotent: leaves logs/start-phone/<run>/ behind (one new run dir per invocation),
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
# with a developer's real start-phone.sh session. The script hardcodes 5123, so we
# cannot actually override it — instead, we require 5123 to be free at test start.
if netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E '[:.]5123[[:space:]]' >/dev/null; then
  printf '\nFATAL: port 5123 is already in use. Free it before running the smoke test.\n' >&2
  exit 1
fi

# Snapshot the existing LATEST so the polling loop can distinguish a NEW run from
# a stale previous one. Without this, the loop would match the prior run's
# PHONE LINK and proceed to assertions before the new script has actually started.
OLD_LATEST=""
[[ -f logs/start-phone/LATEST ]] && OLD_LATEST=$(cat logs/start-phone/LATEST)

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
  # Belt-and-suspenders: free 5123 if anything is left
  taskkill //F //IM cloudflared.exe //T >/dev/null 2>&1 || true
  taskkill //F //IM Agentic.Chat.exe //T >/dev/null 2>&1 || true
}
trap cleanup_smoke EXIT

printf '=== Smoke test: start-phone.sh full lifecycle ===\n\n'

# --------------------------------------------------------------------------
# Priority 1: bash -n syntax check (cheap, catches typos on every PR).
# --------------------------------------------------------------------------
printf -- '--- Priority 1: bash -n syntax check ---\n'
if bash -n start-phone.sh 2>smoke-syntax.err; then
  printf '  \u2713 bash -n start-phone.sh passes\n'
  START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
else
  printf '  \u2717 bash -n start-phone.sh fails:\n' >&2
  cat smoke-syntax.err >&2
  rm -f smoke-syntax.err
  START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  printf '\nAborting: cannot run a syntactically-invalid script.\n' >&2
  exit 1
fi
rm -f smoke-syntax.err

# --------------------------------------------------------------------------
# Priority 2: Start the script, verify it comes up + writes the expected logs.
# --------------------------------------------------------------------------
printf '\n--- Priority 2: start-phone.sh smoke test ---\n'
printf 'Starting start-phone.sh in background...\n'
bash start-phone.sh >/dev/null 2>&1 &
SCRIPT_PID=$!
printf 'PID: %s\n' "$SCRIPT_PID"

# Poll up to 90s for the script to provision a URL.
# CRITICAL: only accept LATEST values that differ from OLD_LATEST — otherwise
# the polling matches the prior run's PHONE LINK and proceeds prematurely.
found=""
for i in $(seq 1 18); do
  CURRENT_LATEST=""
  [[ -f logs/start-phone/LATEST ]] && CURRENT_LATEST=$(cat logs/start-phone/LATEST)
  if [[ -n "$CURRENT_LATEST" && "$CURRENT_LATEST" != "$OLD_LATEST" ]]; then
    LATEST="$CURRENT_LATEST"
    SCRIPT_LOG="logs/start-phone/$LATEST/script.log"
    if [[ -f "$SCRIPT_LOG" ]] && grep -q "PHONE LINK" "$SCRIPT_LOG" 2>/dev/null; then
      found="url"
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

if [[ "$found" == "url" ]]; then
  printf '  \u2713 Script provisioned URL within %ds\n' $((i*5))
  START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
elif [[ "$found" == "error" ]]; then
  printf '  \u2717 Script errored during startup; script.log:\n'
  tail -20 "$SCRIPT_LOG" >&2
  START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  print_summary "smoke-test"
  exit 1
elif [[ "$found" == "died" ]]; then
  printf '  \u2717 Script died during startup\n'
  START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  print_summary "smoke-test"
  exit 1
else
  printf '  \u2717 Script did not provision URL within 90s\n'
  START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  print_summary "smoke-test"
  exit 1
fi

LATEST=$(cat logs/start-phone/LATEST)
LOG_DIR="logs/start-phone/$LATEST"
printf 'Run dir: %s\n' "$LOG_DIR"

# Verify local app responds. curl always writes the http_code via -w (000 on
# connection failure); the `|| true` just suppresses the non-zero exit.
HTTP_CODE=$(curl -sL -o /dev/null -w '%{http_code}' --max-time 5 http://localhost:5123/chat 2>/dev/null || true)
assert_eq "200" "$HTTP_CODE" "Local app responds HTTP 200 on /chat"

# Verify all log files exist
assert_file_exists "$LOG_DIR/script.log" "script.log exists"
assert_file_exists "$LOG_DIR/app.log"    "app.log exists"
assert_file_exists "$LOG_DIR/tunnel.log" "tunnel.log exists"

# Verify script.log has the expected narrative content
if [[ -f "$LOG_DIR/script.log" ]]; then
  SCRIPT_LOG_CONTENT=$(cat "$LOG_DIR/script.log")
  assert_contains "$SCRIPT_LOG_CONTENT" "PHONE LINK"  "script.log contains PHONE LINK"
  assert_contains "$SCRIPT_LOG_CONTENT" "Run ID:"     "script.log logs Run ID"
  assert_contains "$SCRIPT_LOG_CONTENT" "Started at:" "script.log logs start time"
  assert_contains "$SCRIPT_LOG_CONTENT" "https://"    "script.log has the public URL"
fi

# Verify app.log has hot-reload signature (dotnet watch verbose markers)
if [[ -f "$LOG_DIR/app.log" ]]; then
  APP_LOG_CONTENT=$(cat "$LOG_DIR/app.log")
  assert_contains "$APP_LOG_CONTENT" "dotnet watch"      "app.log shows dotnet watch"
  assert_contains "$APP_LOG_CONTENT" "Hot reload"        "app.log mentions hot reload"
  assert_contains "$APP_LOG_CONTENT" "Now listening on:" "app.log shows Kestrel startup"
fi

# Verify tunnel.log has URL provisioning
if [[ -f "$LOG_DIR/tunnel.log" ]]; then
  TUNNEL_LOG_CONTENT=$(cat "$LOG_DIR/tunnel.log")
  assert_contains "$TUNNEL_LOG_CONTENT" "trycloudflare.com" "tunnel.log has trycloudflare URL"
fi

# meta.json should NOT exist while the script is still running (postmortem-only)
if [[ ! -f "$LOG_DIR/meta.json" ]]; then
  printf '  \u2713 meta.json correctly absent during run (postmortem-only by design)\n'
  START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
else
  printf '  \u2717 meta.json exists during run (should be written only on exit)\n'
  START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
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
      printf '  \u2713 Script cleanup completed within %ds of SIGTERM (meta.json written + port freed)\n' "$elapsed"
      START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
      cleanup_done=1
      break
    fi
  fi
  sleep 1
done
if [[ $cleanup_done -eq 0 ]]; then
  printf '  \u2717 Script did not complete cleanup within 20s of SIGTERM (forcing kill)\n'
  START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
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
      printf '  \u2713 meta.json is valid JSON\n'
      START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
    else
      printf '  \u2717 meta.json is not valid JSON\n'
      START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
    fi

    # Required fields present and non-null (a regression here breaks any agent
    # or tool that consumes meta.json as a contract).
    for field in run_id started_at ended_at duration_seconds app_pid tunnel_pid local_url public_url log_dir; do
      val=$(echo "$META" | jq -r ".$field" 2>/dev/null || echo "JQ_ERROR")
      if [[ "$val" != "null" && "$val" != "JQ_ERROR" && -n "$val" ]]; then
        printf '  \u2713 meta.json has non-null .%s\n' "$field"
        START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
      else
        printf '  \u2717 meta.json missing or null .%s\n' "$field"
        START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
      fi
    done

    # Specific value checks
    EXIT_REASON_VAL=$(echo "$META" | jq -r '.exit_reason')
    assert_eq "terminated" "$EXIT_REASON_VAL" "meta.json .exit_reason is 'terminated' (SIGTERM)"

    PUBLIC_URL_VAL=$(echo "$META" | jq -r '.public_url')
    assert_match '^https://[a-z0-9-]+\.trycloudflare\.com$' "$PUBLIC_URL_VAL" "meta.json .public_url matches trycloudflare URL shape"

    LOCAL_URL_VAL=$(echo "$META" | jq -r '.local_url')
    assert_eq "http://localhost:5123" "$LOCAL_URL_VAL" "meta.json .local_url is the expected value"

    DURATION_VAL=$(echo "$META" | jq -r '.duration_seconds')
    if [[ "$DURATION_VAL" =~ ^[0-9]+$ ]] && (( DURATION_VAL > 0 )); then
      printf '  \u2713 meta.json .duration_seconds is a positive integer (%s)\n' "$DURATION_VAL"
      START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
    else
      printf '  \u2717 meta.json .duration_seconds is not a positive integer: %s\n' "$DURATION_VAL"
      START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
    fi

    # Schema shape: .files nested object with script/app/tunnel
    FILES_VALID=$(echo "$META" | jq -e '.files.script == "script.log" and .files.app == "app.log" and .files.tunnel == "tunnel.log"' >/dev/null 2>&1 && echo yes || echo no)
    assert_eq "yes" "$FILES_VALID" "meta.json .files has expected {script,app,tunnel} shape"
  else
    skip "jq not installed — using grep fallback for schema checks"
    assert_contains "$META" '"exit_reason": "terminated"'           "meta.json exit_reason is terminated (grep fallback)"
    assert_contains "$META" '"public_url": "https://'               "meta.json has https public_url (grep fallback)"
    assert_contains "$META" '"local_url": "http://localhost:5123"'  "meta.json local_url is correct (grep fallback)"
  fi
fi

# Verify port freed
sleep 2
if netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E '[:.]5123[[:space:]]' >/dev/null; then
  printf '  \u2717 Port 5123 still occupied after shutdown\n'
  START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
else
  printf '  \u2713 Port 5123 freed after shutdown\n'
  START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
fi

print_summary "smoke-test"
