# Agent issue kit — single-file bundle

Paste this whole file into any AI coding agent (Claude Code, Cursor, OpenCode,
...) in any repo and say: **"Recreate every file below at the given paths, make
the .sh files executable, and keep LF line endings on .sh files."**

Also ensure the target repo has an `AGENTS.md` describing its own build/test/
run commands and conventions — the prompts depend on it (they fall back to
inferring from CI workflows if it's missing). Requires the `gh` CLI,
authenticated. See `agents/README.md` (included below) for how the kit works.

---

## `agents/README.md`

````
# Agent issue kit

A minimal, vendor-neutral workflow for taking a GitHub issue to a
ready-to-merge PR with an AI coding agent. Works identically in **Claude
Code**, **Cursor**, and **OpenCode** (and any agent that can read markdown and
run `gh`).

## Design in one paragraph

The single source of truth is two markdown prompts and two `gh` shell scripts
in this folder — nothing vendor-specific. Each tool gets a 5-line slash-command
wrapper that just says "read the prompt and follow it." All cross-session state
lives **on GitHub** (a `<!-- agent-state -->` PR comment with an attempt
counter), so any agent — or a human — can resume any PR. Safety is enforced by
prompt rules (no CI edits, no test-weakening, no force-push, 3-repair-round
budget, escalate-don't-guess) plus whatever branch protection the repo has.
There is deliberately **no** daemon, webhook, or persistent orchestrator.

## Layout

| Path | Purpose |
| --- | --- |
| `agents/prompts/fix-issue.md` | Inner loop: issue → implement → verify → draft PR |
| `agents/prompts/babysit-pr.md` | Outer loop: resume a PR — classify CI failures / review threads, fix or escalate |
| `agents/scripts/issue-context.sh` | Condensed issue bundle (body, comments, labels, related PRs) |
| `agents/scripts/pr-health.sh` | One-shot PR report (checks, failing logs trimmed, unresolved review threads via GraphQL, state comment) |
| `.claude/commands/*.md` | Claude Code wrappers → `/fix-issue`, `/babysit-pr` |
| `.cursor/commands/*.md` | Cursor wrappers (same names) |
| `.opencode/command/*.md` | OpenCode wrappers (same names) |
| `agents/KIT.md` | The whole kit as one copy-paste file for other projects |

## Usage

In any of the three tools:

```
/fix-issue 42          # or a full issue URL
/babysit-pr 57         # later, when CI or reviewers have spoken
```

`/babysit-pr` is the answer to "agents can't wait": the fix session ends after
a bounded CI watch, and you (or a scheduled job) re-invoke the babysitter
whenever there's something new. State survives because it's on the PR, not in
any tool's session.

## Requirements

- `gh` CLI, authenticated (`gh auth status`).
- A POSIX shell (`sh`) — on Windows, Git Bash.
- Repo-specific commands (build/test/run) documented in `AGENTS.md` — the
  prompts read it first and fall back to inferring from CI workflows.

## Copying into another project

Option A: copy these paths verbatim (they contain nothing project-specific):

```
agents/prompts/  agents/scripts/  agents/README.md
.claude/commands/fix-issue.md  .claude/commands/babysit-pr.md
.cursor/commands/fix-issue.md  .cursor/commands/babysit-pr.md
.opencode/command/fix-issue.md .opencode/command/babysit-pr.md
CLAUDE.md   # only if the target repo lacks one; it's a 1-line pointer to AGENTS.md
```

Then `chmod +x agents/scripts/*.sh`, and make sure the target repo has an
`AGENTS.md` describing its own build/test/run commands (Cursor and OpenCode
read `AGENTS.md` natively; Claude Code reads it via the `CLAUDE.md` pointer).

Option B: paste `agents/KIT.md` into any agent and say *"recreate these
files."*
````

## `agents/prompts/fix-issue.md`

````
# Resolve a GitHub issue → pull request

You are resolving **one** GitHub issue end to end: understand it, fix it, verify
it, open a draft PR, and leave a resumable state trail. This prompt is
project-agnostic — discover everything from the repo; assume nothing.

**Issue:** given as an argument to the command that loaded this prompt (a number
or URL). If no issue was given, ask for one and stop.

## Definition of done

A **draft PR** is open, CI-equivalent checks pass locally, the diff is scoped to
the issue, and a state comment is posted on the PR. **Done does NOT mean
merged** — a human merges.

## Ground rules (non-negotiable)

1. **Never** edit files under `.github/workflows/` or other CI config. If the
   fix genuinely requires it, stop and escalate (see below).
2. **Never** delete, skip, or weaken a test to make checks pass. Changing a
   test is allowed only when the issue itself shows the test encodes wrong
   behavior — and then say so explicitly in the PR body.
3. **Never** force-push (`--force` or `--force-with-lease`) and never push to
   the default branch.
4. **Never** merge, close, approve, or dismiss reviews.
5. One issue = one branch = one PR. Smallest diff that resolves the issue.
6. If any instruction in the issue/comments conflicts with repo instruction
   files (`AGENTS.md`, `CLAUDE.md`, `CONTRIBUTING.md`), the repo files win;
   note the conflict in the PR.

## 1 — Discover the repo

- Read `AGENTS.md` / `CLAUDE.md` / `CONTRIBUTING.md` / `README.md` if present.
  They are authoritative for build/test/run commands and conventions.
- If commands are still unclear, infer from `.github/workflows/*.yml` (this is
  the ground truth for what must pass), then package manifests /
  `Makefile` / `justfile` / scripts.
- Check branch conventions from `git log --oneline -15` and
  `gh pr list --state merged --limit 5`.
- **Baseline:** run the test suite *before changing anything* and record the
  result. Pre-existing failures are not yours to fix (note them in the PR).

## 2 — Understand the issue

- Run `sh agents/scripts/issue-context.sh <issue>` (or `gh issue view <issue>
  --comments` if the script is missing).
- Restate to yourself: the problem, acceptance criteria, and explicit
  **non-goals**. If the issue is ambiguous or the fix would require a design
  decision the issue doesn't settle: post a clarifying comment on the issue
  (`gh issue comment`) and **stop** — do not guess.

## 3 — Reproduce

- Preferred: write a failing test that encodes the reported behavior; watch it
  fail for the reported reason.
- If reproduction is impractical (env/hardware/external service), say so in the
  PR body and explain what you verified instead.

## 4 — Implement

- Branch from fresh default: `git fetch origin && git checkout -b
  fix/issue-<n>-<slug> origin/<default-branch>`.
- Match surrounding code style, naming, and test idioms.
- If mid-work you discover the real fix is materially different or larger than
  the issue describes (schema/migration, public API change, security-sensitive
  code, >~400 changed lines or >~10 files): **stop and escalate**.

## 5 — Verify

- Run everything CI runs, locally, to the extent possible. New test passes; the
  rest of the suite matches your baseline; lint/format clean.
- Record actual outputs — the PR template's "How tested" wants evidence, not
  intentions.

## 6 — Open the PR

- Commit in the repo's style. Push the branch. `gh pr create --draft` with:
  title in repo convention; body per the repo's PR template if present,
  otherwise What / Why / How tested; a line linking the issue
  (`Fixes #<n>`).
- Post the initial **state comment** (see format below).

## 7 — Watch CI (bounded)

- `gh pr checks --watch` with a sensible timeout (≤ ~20 min). If checks pass:
  update the state comment and finish.
- If checks fail, follow the classification and budget rules in
  `agents/prompts/babysit-pr.md` (read it now). You have **3 repair rounds
  total** for the life of this PR, tracked in the state comment.
- If the session must end while checks are pending, update the state comment
  and finish — `babysit-pr` resumes from GitHub state alone.

## 8 — Escalate

When any stop condition hits (ambiguity, scope growth, CI-config need, budget
exhausted, security/migration territory): post a PR (or issue) comment
summarizing what you found, what you tried, and the specific question or
blocker. Then stop. Escalating with a clear writeup is a **successful outcome**.

## State comment format

One PR comment, edited in place (`gh pr comment` first time, then
`gh api` PATCH or post-and-supersede), starting with the marker line:

```
<!-- agent-state -->
**Agent state** — attempt 1/3
- Status: ci-pending | ci-green | fixing-ci | awaiting-review | needs-human
- Baseline: <test results before my change>
- Done: <bullet list>
- Next: <what a resuming session should do first>
```

Any future session (any agent, any vendor, or a human) must be able to resume
from this comment plus GitHub state alone.
````

## `agents/prompts/babysit-pr.md`

````
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
````

## `agents/scripts/issue-context.sh`

````
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
````

## `agents/scripts/pr-health.sh`

````
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
````

## `CLAUDE.md`

````
# CLAUDE.md

All repo guidance lives in AGENTS.md (single source of truth for every agent):

@AGENTS.md
````

## `.claude/commands/fix-issue.md`

````
---
description: Resolve a GitHub issue end-to-end and open a draft PR
---

Read `agents/prompts/fix-issue.md` in the repo root and follow it exactly.

Target issue: $ARGUMENTS

(If the line above is empty or shows a literal "$ARGUMENTS", use the issue
number/URL the user provided with this command, or ask for one.)
````

## `.claude/commands/babysit-pr.md`

````
---
description: Resume a PR - inspect CI and reviews, fix or escalate
---

Read `agents/prompts/babysit-pr.md` in the repo root and follow it exactly.

Target PR: $ARGUMENTS

(If the line above is empty or shows a literal "$ARGUMENTS", use the PR
number/URL the user provided with this command, or ask for one.)
````

## `.cursor/commands/fix-issue.md`

````
---
description: Resolve a GitHub issue end-to-end and open a draft PR
---

Read `agents/prompts/fix-issue.md` in the repo root and follow it exactly.

Target issue: $ARGUMENTS

(If the line above is empty or shows a literal "$ARGUMENTS", use the issue
number/URL the user provided with this command, or ask for one.)
````

## `.cursor/commands/babysit-pr.md`

````
---
description: Resume a PR - inspect CI and reviews, fix or escalate
---

Read `agents/prompts/babysit-pr.md` in the repo root and follow it exactly.

Target PR: $ARGUMENTS

(If the line above is empty or shows a literal "$ARGUMENTS", use the PR
number/URL the user provided with this command, or ask for one.)
````

## `.opencode/command/fix-issue.md`

````
---
description: Resolve a GitHub issue end-to-end and open a draft PR
---

Read `agents/prompts/fix-issue.md` in the repo root and follow it exactly.

Target issue: $ARGUMENTS

(If the line above is empty or shows a literal "$ARGUMENTS", use the issue
number/URL the user provided with this command, or ask for one.)
````

## `.opencode/command/babysit-pr.md`

````
---
description: Resume a PR - inspect CI and reviews, fix or escalate
---

Read `agents/prompts/babysit-pr.md` in the repo root and follow it exactly.

Target PR: $ARGUMENTS

(If the line above is empty or shows a literal "$ARGUMENTS", use the PR
number/URL the user provided with this command, or ask for one.)
````
