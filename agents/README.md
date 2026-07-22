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
