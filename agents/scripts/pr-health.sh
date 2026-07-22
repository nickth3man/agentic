#!/usr/bin/env sh
# pr-health.sh <pr-number>
# One-shot health report for a PR: overview, check results, trimmed logs of
# failing runs, unresolved review threads, and the latest agent state comment.
# Requires: gh (authenticated). Output is deliberately condensed for LLM use.
set -eu

PR="${1:?usage: pr-health.sh <pr-number>}"
LOG_TAIL="${LOG_TAIL:-80}"   # lines of failed-step log per failing run

echo "=== PR OVERVIEW ==="
gh pr view "$PR" --json number,title,state,isDraft,mergeable,mergeStateStatus,baseRefName,headRefName,url \
  --jq '"#\(.number) \(.title)\nstate: \(.state)  draft: \(.isDraft)  mergeable: \(.mergeable)  mergeState: \(.mergeStateStatus)\nbranch: \(.headRefName) -> \(.baseRefName)\n\(.url)"'

BRANCH=$(gh pr view "$PR" --json headRefName --jq .headRefName)

echo ""
echo "=== CHECKS ==="
gh pr checks "$PR" || true   # non-zero exit when checks fail/pending is expected

echo ""
echo "=== FAILING RUN LOGS (last $LOG_TAIL lines of failed steps each) ==="
FAILED_RUNS=$(gh run list --branch "$BRANCH" --status failure --limit 3 \
  --json databaseId,name --jq '.[] | "\(.databaseId) \(.name)"' || true)
if [ -z "$FAILED_RUNS" ]; then
  echo "(no failing runs on branch $BRANCH)"
else
  echo "$FAILED_RUNS" | while read -r RUN_ID RUN_NAME; do
    echo "--- run $RUN_ID ($RUN_NAME) ---"
    gh run view "$RUN_ID" --log-failed 2>/dev/null | tail -n "$LOG_TAIL" || echo "(logs unavailable)"
  done
fi

echo ""
echo "=== UNRESOLVED REVIEW THREADS ==="
gh api graphql \
  -F owner='{owner}' -F repo='{repo}' -F pr="$PR" \
  -f query='query($owner:String!,$repo:String!,$pr:Int!){
    repository(owner:$owner,name:$repo){
      pullRequest(number:$pr){
        reviewThreads(first:50){nodes{
          isResolved isOutdated path line
          comments(first:10){nodes{author{login} body}}
        }}
      }
    }
  }' \
  --jq '[.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved | not)]
        | if length == 0 then "(none)"
          else .[] | "--- \(.path // "general"):\(.line // "?")\(if .isOutdated then " (outdated)" else "" end)\n"
               + ([.comments.nodes[] | "[\(.author.login)] \(.body)"] | join("\n"))
          end' || echo "(review thread query failed — inspect manually with: gh pr view $PR --comments)"

echo ""
echo "=== LATEST AGENT STATE COMMENT ==="
gh pr view "$PR" --json comments \
  --jq '[.comments[] | select(.body | contains("<!-- agent-state -->"))] | if length == 0 then "(none — this PR has no agent state comment yet)" else last | .body end'
