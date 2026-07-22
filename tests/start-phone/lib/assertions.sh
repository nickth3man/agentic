#!/usr/bin/env bash
# Shared assertions for start-phone tests. Sourced by other test scripts.
# Uses simple counters + non-zero exit on failure — no bats dependency.
# Idempotent: safe to source multiple times.

if [[ -z "${START_PHONE_TEST_LIB_LOADED:-}" ]]; then
  START_PHONE_TEST_LIB_LOADED=1
  START_PHONE_TEST_PASS=0
  START_PHONE_TEST_FAIL=0
fi

# assert_eq <expected> <actual> <name>
assert_eq() {
  local expected="$1" actual="$2" name="$3"
  if [[ "$expected" == "$actual" ]]; then
    printf '  \u2713 %s\n' "$name"
    START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
  else
    printf '  \u2717 %s\n' "$name"
    printf '    expected: %s\n' "$expected"
    printf '    actual:   %s\n' "$actual"
    START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  fi
}

# assert_contains <haystack> <needle> <name>
assert_contains() {
  local haystack="$1" needle="$2" name="$3"
  if printf '%s' "$haystack" | grep -qF -- "$needle"; then
    printf '  \u2713 %s\n' "$name"
    START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
  else
    printf '  \u2717 %s\n' "$name"
    printf '    expected to contain: %s\n' "$needle"
    printf '    actual:              %s\n' "$haystack"
    START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  fi
}

# assert_file_exists <path> <name>
assert_file_exists() {
  local path="$1" name="$2"
  if [[ -f "$path" ]]; then
    printf '  \u2713 %s\n' "$name"
    START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
  else
    printf '  \u2717 %s (file missing: %s)\n' "$name" "$path"
    START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  fi
}

# assert_match <regex> <actual> <name>
assert_match() {
  local regex="$1" actual="$2" name="$3"
  if printf '%s' "$actual" | grep -qE -- "$regex"; then
    printf '  \u2713 %s\n' "$name"
    START_PHONE_TEST_PASS=$((START_PHONE_TEST_PASS+1))
  else
    printf '  \u2717 %s\n' "$name"
    printf '    expected to match regex: %s\n' "$regex"
    printf '    actual:                  %s\n' "$actual"
    START_PHONE_TEST_FAIL=$((START_PHONE_TEST_FAIL+1))
  fi
}

# skip <reason>
skip() {
  local reason="$1"
  printf '  \u26a0 Skip: %s\n' "$reason"
}

# print_summary <suite-name>  → exits with fail count (0 = success)
print_summary() {
  local name="$1"
  printf '\n%s: %d passed, %d failed\n' "$name" "$START_PHONE_TEST_PASS" "$START_PHONE_TEST_FAIL"
  return "$START_PHONE_TEST_FAIL"
}
