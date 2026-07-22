# Babysit a pull request

You are resuming work on an existing PR — yours or another agent's. Your job:
inspect its current health, address what is actionable, and either get it back
to green/ready or escalate cleanly. All state you need lives on GitHub; do not
assume memory of any previous session.

**PR:** given as an argument (number or URL). If none was given, list open PRs
(`gh pr list`) and ask which one; do not guess.

The **ground rules** in `agents/prompts/fix-issue.md` apply verbatim (no CI
edits, no test-weakening, no force-push, no merging). Read them if you haven't.

## 1 — Gather state

- Run `sh agents/scripts/pr-health.sh <pr>` — it prints: PR overview, check
  results, trimmed logs of failing runs, unresolved review threads, and the
  latest `<!-- agent-state -->` comment.
- From the state comment, note the **attempt count**. If it is already 3/3,
  skip straight to Escalate.
- Re-read the linked issue if the reviews dispute scope.

## 2 — Classify every open item before acting

For each failing check:

- **Caused by this PR's diff?** Compare against the baseline in the state
  comment; check whether the same job fails on the default branch
  (`gh run list --branch <default> --limit 5`). If pre-existing → not yours;
  note it, don't fix it here.
- **Flaky?** (Passes locally, known-flaky markers, infra errors like timeouts /
  429s / runner death.) → `gh run rerun <id> --failed` **once**. If it fails
  again the same way, treat as real or escalate. Never "fix" flakiness by
  loosening the test.
- **Real and mine** → fix it (this consumes a repair round).

For each unresolved review thread:

- **Actionable and in scope** → implement, push, reply to the thread stating
  what changed, resolve it if the platform allows.
- **Question** → answer in the thread; no code change.
- **Wrong, or out of scope for this issue** → reply with your reasoning and
  leave it unresolved for a human. Automated reviewers (CodeRabbit, Sourcery,
  cubic, Copilot, etc.) are **advisory**: disagreeing with a clear explanation
  is an acceptable outcome. Never implement a suggestion you believe is wrong
  just to clear the thread.

## 3 — Act (budget-limited)

- Total repair rounds for the life of the PR: **3** (a round = commit + push +
  re-watch CI). Increment the counter in the state comment *before* pushing.
- After pushing, `gh pr checks --watch` with a ≤20 min timeout.
- Multiple fixes discovered at once = one round, one push.

## 4 — Update state and finish

Every session ends by updating the `<!-- agent-state -->` comment: attempt
count, status (`ci-green` / `awaiting-review` / `needs-human` / …), what was
done, and what a future session should do first. Then summarize for the user:
what changed, what's green, what still needs a human.

## Escalate when

- Budget exhausted; or the same check failed twice for different-looking
  reasons (you're guessing).
- A reviewer asks for scope beyond the issue, or two reviewers conflict.
- The fix requires CI config, migrations, secrets, or security-sensitive code.
- GitHub state contradicts the state comment in a way you can't explain
  (someone else pushed to the branch — **stop immediately**, never overwrite).

Escalation = state comment set to `needs-human` + a PR comment with a concise
handoff: findings, attempts, and the specific decision needed. Then stop.
