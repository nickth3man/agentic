#!/usr/bin/env bash
# Run all start-phone.sh tests sequentially. Used by CI and local dev.
# Usage: bash tests/start-phone/run-tests.sh
# Exit code: number of failed test suites (0 = all passed).

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

printf '###############################################\n'
printf '# start-phone.sh test suite\n'
printf '###############################################\n'

SUITES_PASS=0
SUITES_FAIL=0
TOTAL_PASS=0
TOTAL_FAIL=0

run_suite() {
  local file="$1" name="$2"
  printf '\n=== Running: %s ===\n' "$name"
  # Capture output so we can both display it AND parse the summary line
  local output
  output=$(bash "$file" 2>&1) || true
  printf '%s\n' "$output"

  # Parse "<name>: N passed, M failed" → last line of summary
  local last
  last=$(printf '%s\n' "$output" | grep -E "^(smoke-test|error-paths-test): [0-9]+ passed, [0-9]+ failed" | tail -1 || true)
  if [[ -n "$last" ]]; then
    local p f
    p=$(printf '%s' "$last" | grep -oE '[0-9]+ passed'   | grep -oE '[0-9]+')
    f=$(printf '%s' "$last" | grep -oE '[0-9]+ failed'   | grep -oE '[0-9]+')
    TOTAL_PASS=$((TOTAL_PASS + p))
    TOTAL_FAIL=$((TOTAL_FAIL + f))
    if [[ "$f" -eq 0 ]]; then
      SUITES_PASS=$((SUITES_PASS+1))
    else
      SUITES_FAIL=$((SUITES_FAIL+1))
    fi
  else
    SUITES_FAIL=$((SUITES_FAIL+1))
    TOTAL_FAIL=$((TOTAL_FAIL+1))
  fi
}

run_suite "$SCRIPT_DIR/smoke-test.sh"       "smoke test (priorities 1, 2, 3)"
run_suite "$SCRIPT_DIR/error-paths-test.sh" "error-paths test (priority 5)"

printf '\n###############################################\n'
printf '# Suite results: %d passed, %d failed\n' "$SUITES_PASS" "$SUITES_FAIL"
printf '# Total assertions: %d passed, %d failed\n' "$TOTAL_PASS" "$TOTAL_FAIL"
printf '###############################################\n'

# Exit non-zero if any suite failed (CI signal)
if (( SUITES_FAIL > 0 )); then
  exit 1
fi
exit 0
