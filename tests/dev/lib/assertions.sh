#!/usr/bin/env bash
# Shared assertions for start-dev tests. Sourced by other test scripts.
# Uses simple counters + non-zero exit on failure — no bats dependency.
# Idempotent: safe to source multiple times.

if [[ -z "${DEV_TEST_LIB_LOADED:-}" ]]; then
  DEV_TEST_LIB_LOADED=1
  DEV_TEST_PASS=0
  DEV_TEST_FAIL=0
fi

# assert_eq <expected> <actual> <name>
assert_eq() {
  local expected="$1" actual="$2" name="$3"
  if [[ "$expected" == "$actual" ]]; then
    printf '  ✓ %s\n' "$name"
    DEV_TEST_PASS=$((DEV_TEST_PASS+1))
  else
    printf '  ✗ %s\n' "$name"
    printf '    expected: %s\n' "$expected"
    printf '    actual:   %s\n' "$actual"
    DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  fi
}

# assert_contains <haystack> <needle> <name>
assert_contains() {
  local haystack="$1" needle="$2" name="$3"
  if printf '%s' "$haystack" | grep -qF -- "$needle"; then
    printf '  ✓ %s\n' "$name"
    DEV_TEST_PASS=$((DEV_TEST_PASS+1))
  else
    printf '  ✗ %s\n' "$name"
    printf '    expected to contain: %s\n' "$needle"
    printf '    actual:              %s\n' "$haystack"
    DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  fi
}

# assert_not_contains <haystack> <needle> <name> — passes when the needle is NOT
# present in the haystack. Used to assert the absence of tunnel fields after the
# tunnel removal (e.g. "meta.json must not contain \"tunnel_pid\"").
assert_not_contains() {
  local haystack="$1" needle="$2" name="$3"
  if ! printf '%s' "$haystack" | grep -qF -- "$needle"; then
    printf '  ✓ %s\n' "$name"
    DEV_TEST_PASS=$((DEV_TEST_PASS+1))
  else
    printf '  ✗ %s\n' "$name"
    printf '    expected to NOT contain: %s\n' "$needle"
    printf '    actual:                  %s\n' "$haystack"
    DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  fi
}

# assert_file_exists <path> <name>
assert_file_exists() {
  local path="$1" name="$2"
  if [[ -f "$path" ]]; then
    printf '  ✓ %s\n' "$name"
    DEV_TEST_PASS=$((DEV_TEST_PASS+1))
  else
    printf '  ✗ %s (file missing: %s)\n' "$name" "$path"
    DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  fi
}

# assert_match <regex> <actual> <name>
assert_match() {
  local regex="$1" actual="$2" name="$3"
  if printf '%s' "$actual" | grep -qE -- "$regex"; then
    printf '  ✓ %s\n' "$name"
    DEV_TEST_PASS=$((DEV_TEST_PASS+1))
  else
    printf '  ✗ %s\n' "$name"
    printf '    expected to match regex: %s\n' "$regex"
    printf '    actual:                  %s\n' "$actual"
    DEV_TEST_FAIL=$((DEV_TEST_FAIL+1))
  fi
}

# skip <reason>
skip() {
  local reason="$1"
  printf '  ⚠ Skip: %s\n' "$reason"
}

# print_summary <suite-name>  → exits with fail count (0 = success)
print_summary() {
  local name="$1"
  printf '\n%s: %d passed, %d failed\n' "$name" "$DEV_TEST_PASS" "$DEV_TEST_FAIL"
  return "$DEV_TEST_FAIL"
}
