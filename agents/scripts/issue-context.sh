#!/usr/bin/env sh
# issue-context.sh <issue-number-or-url>
# Condensed context bundle for a GitHub issue: body, all comments, labels,
# and any PRs that already reference it. Requires: gh (authenticated).
set -eu

ISSUE="${1:?usage: issue-context.sh <issue-number-or-url>}"

echo "=== ISSUE ==="
gh issue view "$ISSUE" --comments

echo ""
echo "=== METADATA ==="
gh issue view "$ISSUE" --json number,state,labels,assignees,milestone \
  --jq '"state: \(.state)  labels: \([.labels[].name] | join(", "))  assignees: \([.assignees[].login] | join(", "))"'

# Extract the bare number (works for both "123" and ".../issues/123").
NUM=$(printf '%s' "$ISSUE" | sed 's|.*/||')

echo ""
echo "=== PRs REFERENCING THIS ISSUE (any state) ==="
gh pr list --search "$NUM in:title,body" --state all --limit 10 \
  --json number,title,state,isDraft,url \
  --jq '.[] | "#\(.number) [\(.state)\(if .isDraft then "/draft" else "" end)] \(.title)  \(.url)"' || true
